using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Spell pricing for the Tactician (#191 A5) - what a cast is WORTH, before choosing to make
    /// it. Damage spells (<see cref="Effect.DealHits"/>) price through CombatMath's save/wound
    /// mirror; every other effect (buff, debuff, conditional, forced move) prices a crude static
    /// fraction of the target's value - the documented A5 placeholder (plan sec. 8): anticipatory
    /// buff valuation arrives with Phase C's learned evaluator.
    /// <para>
    /// Target eligibility mirrors <see cref="Stages.SpellTargeting"/> (affinity + any-living-model
    /// range, base-to-base 3D + line of sight when required) - reimplemented here because
    /// SpellTargeting needs the IGameContext resolvers do not receive (the same recorded gap as
    /// the planner's bare RuleEvaluator). The mirror only VALUES casts; what is actually picked
    /// always comes from the engine's own option lists, so a divergence can cost value but never
    /// legality.
    /// </para>
    /// <para>
    /// #429 (Core Rules v3.6.0): line of sight is read from each spell's own
    /// <see cref="TargetSelector.RequireLineOfSight"/> (the importer sets it from the spell's "in line of sight"
    /// clause) - there is no caster-level LoS assumption, so a spell without the clause values targets behind
    /// terrain.
    /// </para>
    /// <para>
    /// #442 (#429's casting gaps): the success chance is the engine's own threshold
    /// (<see cref="CastThreshold"/>: the base 4+ less Spell Amplifier's passive bonus, the caster's granted cast
    /// tokens and a relay's bonus, clamped 2-6 - what <c>CastSpellStage</c> puts in the picker), and targets are
    /// reached from every cast origin <see cref="SpellRelay.OriginsFor"/> offers (a Spell Conduit relay measures range
    /// and sight from ITS models, and adds its bonus when the chosen targets need it). Still unpriced: a self-boost
    /// (the bots reply 0) and other casters' assists / hindrance, which are not known at the choice.
    /// </para>
    /// </summary>
    public static class SpellValuation
    {
        /// <summary>
        /// #442: the casting roll <paramref name="caster"/> needs from an origin worth <paramref name="relayBonus"/>
        /// - exactly <c>CastSpellStage</c>'s threshold before any boost or assist: 4+ less the passive cast modifier
        /// (<see cref="CastRollRules.Net"/>, read-only) and the granted cast tokens (peeked, not spent), clamped.
        /// </summary>
        public static int CastThreshold(RuleEvaluator evaluator, IUnit caster, int relayBonus = 0) =>
            DiceUtilities.ClampSuccessRollNeeded(CastSpellStage.CastSuccessThreshold
                - CastRollRules.Net(caster, evaluator, live: false)
                - GrantedRollModifiers.PeekNet(caster, ERollKind.Cast) - relayBonus);

        /// <summary>#442: the chance the cast succeeds at <see cref="CastThreshold"/> - (7 - threshold) / 6.</summary>
        public static float CastSuccessChance(RuleEvaluator evaluator, IUnit caster, int relayBonus = 0) =>
            (7 - CastThreshold(evaluator, caster, relayBonus)) / 6f;

        /// <summary>
        /// The player's army, by the engine's own lookup (first match) so spell enumeration agrees
        /// with ChooseActionStage.GetCanCast.
        /// </summary>
        public static ArmyData? ArmyOf(ITableState tableState, PlayerID player)
        {
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (army.PlayerID == player && army is ArmyData data) return data;
            }
            return null;
        }

        /// <summary>Finds a spell by the cast stage's picker label, "{Name} ({Threshold})".</summary>
        public static RuntimeSpell? FindByLabel(ArmyData army, string label)
        {
            foreach (RuntimeSpell spell in army.Spells)
            {
                if ($"{spell.Name} ({spell.Threshold})" == label) return spell;
            }
            return null;
        }

        public static RuntimeSpell? FindByName(ArmyData army, string name)
        {
            foreach (RuntimeSpell spell in army.Spells)
            {
                if (spell.Name == name) return spell;
            }
            return null;
        }

        // Team-aware friendliness, mirroring SpellTargeting: teammates count as friendly; with no
        // registered team, only the player itself does.
        public static bool IsFriendlyTo(ITableState tableState, PlayerID player, PlayerID other)
        {
            foreach (ITeam team in tableState.Teams.Objects)
            {
                if (team.IsPlayerOnTeam(player)) return team.IsPlayerOnTeam(other);
            }
            return player == other;
        }

        /// <summary>
        /// Value of the spell landing on one target, in the planner's value-fraction currency
        /// (fraction of the unit killed x UnitValue / 100). Damage into a friendly target prices
        /// NEGATIVE; any non-damage effect prices the static placeholder fraction of the target.
        /// </summary>
        public static float TargetValue(RuleEvaluator evaluator, DataBinding<UnitData> caster,
            RuntimeSpell spell, DataBinding<UnitData> target, bool targetIsFriendlyToCaster)
        {
            UnitData unit = target.GetValue();
            if (spell.Effect is Effect.DealHits)
            {
                AttackEstimate estimate = CombatMath.EstimateSpellDamage(evaluator, caster, target, spell);
                float fraction = Math.Min(1f, estimate.ExpectedWounds / Math.Max(1f, unit.RemainingWounds));
                float value = fraction * TacticalAnalysis.UnitValue(unit) / 100f;
                return targetIsFriendlyToCaster ? -value : value;
            }
            return TacticianWeights.CastEffectStaticFraction * TacticalAnalysis.UnitValue(unit) / 100f;
        }

        /// <summary>
        /// Expected value of the spell's effect if the cast succeeds: the best-first eligible
        /// targets' summed value, up to MaxCount, never adding a losing target beyond MinCount.
        /// 0 when the spell has no eligible target at all.
        /// </summary>
        public static float GrossCastValue(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> caster, RuntimeSpell spell, bool seeThroughFriendlyUnits)
            => Gross(evaluator, tableState, caster, spell, seeThroughFriendlyUnits).Value;

        /// <summary>
        /// Net expected value of ATTEMPTING the spell now: success chance x effect value, minus
        /// the tokens the attempt burns win or lose. #442: the chance is the engine's own
        /// (<see cref="CastSuccessChance"/>), from the best-bonus origin that reaches every target the value counts.
        /// </summary>
        public static float NetCastValue(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> caster, RuntimeSpell spell, bool seeThroughFriendlyUnits)
        {
            (float gross, int relayBonus) = Gross(evaluator, tableState, caster, spell, seeThroughFriendlyUnits);
            return CastSuccessChance(evaluator, caster.GetValue(), relayBonus) * gross
                - spell.Threshold * TacticianWeights.CastTokenValue;
        }

        // The best-first targets' summed value (see GrossCastValue) and the largest relay bonus among the origins
        // that reach ALL of them - CastSpellStage narrows the viable origins to the ones covering every pick.
        private static (float Value, int RelayBonus) Gross(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> caster, RuntimeSpell spell, bool seeThroughFriendlyUnits)
        {
            IReadOnlyList<SpellRelay.CastOrigin> origins = SpellRelay.OriginsFor(tableState, evaluator,
                caster.GetValue());
            List<(DataBinding<UnitData> Unit, float Value, ulong Origins)> ranked =
                Rank(evaluator, tableState, caster, spell, seeThroughFriendlyUnits, origins);
            if (ranked.Count < Math.Max(1, spell.Target.MinCount)) return (0f, 0);

            float total = 0f;
            ulong covering = ulong.MaxValue;
            for (int i = 0; i < ranked.Count && i < spell.Target.MaxCount; i++)
            {
                if (ranked[i].Value <= 0f && i >= spell.Target.MinCount) break;
                total += ranked[i].Value;
                covering &= ranked[i].Origins;
            }

            int bonus = 0;
            for (int o = 0; o < origins.Count && o < 64; o++)
                if ((covering & (1UL << o)) != 0) bonus = Math.Max(bonus, origins[o].RollBonus);
            return (total, bonus);
        }

        /// <summary>Eligible targets with their values, best first. #442: eligible from any cast origin (relays).</summary>
        public static List<(DataBinding<UnitData> Unit, float Value)> RankEligibleTargets(
            RuleEvaluator evaluator, ITableState tableState, DataBinding<UnitData> caster,
            RuntimeSpell spell, bool seeThroughFriendlyUnits)
            => Rank(evaluator, tableState, caster, spell, seeThroughFriendlyUnits,
                    SpellRelay.OriginsFor(tableState, evaluator, caster.GetValue()))
                .Select(r => (r.Unit, r.Value)).ToList();

        // Each eligible target with its value and the bit set of origins (by index in `origins`) that reach it.
        private static List<(DataBinding<UnitData> Unit, float Value, ulong Origins)> Rank(
            RuleEvaluator evaluator, ITableState tableState, DataBinding<UnitData> caster,
            RuntimeSpell spell, bool seeThroughFriendlyUnits, IReadOnlyList<SpellRelay.CastOrigin> origins)
        {
            PlayerID casterPlayer = caster.GetValue().PlayerID;
            var ranked = new List<(DataBinding<UnitData> Unit, float Value, ulong Origins)>();
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                {
                    UnitData candidate = unit.GetValue();
                    if (!candidate.GetIsAlive() || !candidate.GetIsOnBattlefield()) continue;
                    bool friendly = IsFriendlyTo(tableState, casterPlayer, candidate.PlayerID);
                    if (!MatchesAffinity(spell.Target.TargetAffinity, caster, unit, friendly)) continue;
                    ulong reach = 0;
                    for (int o = 0; o < origins.Count && o < 64; o++)
                        if (WithinRangeAndSight(tableState, origins[o].Unit, unit, spell.Target, seeThroughFriendlyUnits))
                            reach |= 1UL << o;
                    if (reach == 0) continue;
                    ranked.Add((unit, TargetValue(evaluator, caster, spell, unit, friendly), reach));
                }
            }
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));
            return ranked;
        }

        private static bool MatchesAffinity(ETargetAffinity affinity, DataBinding<UnitData> caster,
            DataBinding<UnitData> candidate, bool friendly)
        {
            bool self = candidate.Reference.Equals(caster.Reference);
            return affinity switch
            {
                ETargetAffinity.Self => self,
                ETargetAffinity.Friend => friendly,
                ETargetAffinity.Foe => !friendly,
                ETargetAffinity.Any => true,
                _ => false,
            };
        }

        // SpellTargeting.WithinRangeAndSight's test, verbatim: some living ORIGIN model (the caster's own, or a
        // Spell Conduit relay's - #442) within range (base-to-base, 3D) of some living target model, with line of
        // sight when required; the origin's own models are the ones that cannot block.
        internal static bool WithinRangeAndSight(ITableState tableState, IUnit origin,
            DataBinding<UnitData> target, TargetSelector selector, bool seeThroughFriendlyUnits)
        {
            List<ITerrain> terrain = tableState.Terrain.Objects.ToList();
            IReadOnlyList<ITerrain> blockers = selector.RequireLineOfSight
                ? terrain.Concat(LineOfSightUtilities.BuildModelBlockers(tableState, origin, (IUnit)target.GetValue(),
                    seeThroughFriendlyUnits)).ToList()
                : terrain;

            foreach (IModel cm in origin.Models)
            {
                if (!cm.GetIsAlive()) continue;
                Position casterPos = cm.Position;
                foreach (DataBinding<ModelData> targetModel in target.GetValue().ModelBindings
                    .Where(m => m.GetValue().GetIsAlive()))
                {
                    ModelData tm = targetModel.GetValue();
                    Position targetPos = tm.PositionBinding.GetValue();
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        casterPos, targetPos, cm.BaseShape, cm.Facing, tm.BaseShape, tm.Facing);
                    if (distance > selector.RangeInches) continue;
                    if (!selector.RequireLineOfSight) return true;
                    if (LineOfSightUtilities.HasLineOfSight(casterPos, targetPos, blockers)) return true;
                }
            }
            return false;
        }
    }
}
