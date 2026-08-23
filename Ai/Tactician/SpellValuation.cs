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
    /// </summary>
    public static class SpellValuation
    {
        /// <summary>Chance a cast succeeds before assists - the base 4+ roll.</summary>
        public const float BaseCastSuccessChance = 0.5f;

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
        {
            List<(DataBinding<UnitData> Unit, float Value)> ranked =
                RankEligibleTargets(evaluator, tableState, caster, spell, seeThroughFriendlyUnits);
            if (ranked.Count < Math.Max(1, spell.Target.MinCount)) return 0f;

            float total = 0f;
            for (int i = 0; i < ranked.Count && i < spell.Target.MaxCount; i++)
            {
                if (ranked[i].Value <= 0f && i >= spell.Target.MinCount) break;
                total += ranked[i].Value;
            }
            return total;
        }

        /// <summary>
        /// Net expected value of ATTEMPTING the spell now: success chance x effect value, minus
        /// the tokens the attempt burns win or lose.
        /// </summary>
        public static float NetCastValue(RuleEvaluator evaluator, ITableState tableState,
            DataBinding<UnitData> caster, RuntimeSpell spell, bool seeThroughFriendlyUnits)
            => BaseCastSuccessChance * GrossCastValue(evaluator, tableState, caster, spell, seeThroughFriendlyUnits)
               - spell.Threshold * TacticianWeights.CastTokenValue;

        /// <summary>Eligible targets with their values, best first.</summary>
        public static List<(DataBinding<UnitData> Unit, float Value)> RankEligibleTargets(
            RuleEvaluator evaluator, ITableState tableState, DataBinding<UnitData> caster,
            RuntimeSpell spell, bool seeThroughFriendlyUnits)
        {
            PlayerID casterPlayer = caster.GetValue().PlayerID;
            var ranked = new List<(DataBinding<UnitData> Unit, float Value)>();
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                {
                    UnitData candidate = unit.GetValue();
                    if (!candidate.GetIsAlive() || !candidate.GetIsOnBattlefield()) continue;
                    bool friendly = IsFriendlyTo(tableState, casterPlayer, candidate.PlayerID);
                    if (!MatchesAffinity(spell.Target.TargetAffinity, caster, unit, friendly)) continue;
                    if (!WithinRangeAndSight(tableState, caster, unit, spell.Target, seeThroughFriendlyUnits)) continue;
                    ranked.Add((unit, TargetValue(evaluator, caster, spell, unit, friendly)));
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

        // SpellTargeting.WithinRangeAndSight's test, verbatim: some living caster model within
        // range (base-to-base, 3D) of some living target model, with line of sight when required.
        private static bool WithinRangeAndSight(ITableState tableState, DataBinding<UnitData> caster,
            DataBinding<UnitData> target, TargetSelector selector, bool seeThroughFriendlyUnits)
        {
            List<ITerrain> terrain = tableState.Terrain.Objects.ToList();
            IReadOnlyList<ITerrain> blockers = selector.RequireLineOfSight
                ? terrain.Concat(LineOfSightUtilities.BuildModelBlockers(tableState, caster, target,
                    seeThroughFriendlyUnits)).ToList()
                : terrain;

            foreach (DataBinding<ModelData> casterModel in caster.GetValue().ModelBindings
                .Where(m => m.GetValue().GetIsAlive()))
            {
                ModelData cm = casterModel.GetValue();
                Position casterPos = cm.PositionBinding.GetValue();
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
