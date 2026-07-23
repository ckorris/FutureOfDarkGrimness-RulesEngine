using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #033 — shared spell target-eligibility, used by both <see cref="ChooseActionStage.GetCanCast"/> (to
    /// gate the Cast action and avoid offering a spell with no legal target — which would otherwise loop
    /// forever under a deterministic resolver) and <see cref="CastSpellStage"/> (to build the target list and
    /// to drop uncastable spells from the picker). The eligibility test mirrors the per-model range/line-of-
    /// sight check <see cref="ChooseRangedAttackStage"/> uses for shooting: a target is legal when it matches
    /// the selector's affinity and some living caster model is within range (base-to-base, 3D) of some living
    /// target model and — when the spell requires it — has line of sight.
    /// </summary>
    public static class SpellTargeting
    {
        /// <summary>
        /// The units <paramref name="caster"/> may target with <paramref name="selector"/>. Affinity is
        /// always judged against the CASTER (a Self- or Friend-affinity spell means the caster's own side
        /// however the cast is relayed); range and line of sight are measured from
        /// <paramref name="origin"/>, which defaults to the caster and is a <c>Spell Conduit</c> when one is
        /// relaying the cast (#197 P23).
        /// </summary>
        public static List<DataBinding<UnitData>> GetEligibleTargets(IGameContext gameContext,
            DataBinding<UnitData> caster, PlayerID casterPlayer, TargetSelector selector,
            IUnit? origin = null)
        {
            IUnit measureFrom = origin ?? caster.GetValue();
            TeamData team = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(casterPlayer));
            bool IsFriendly(PlayerID p) => team != null ? team.IsPlayerOnTeam(p) : p == casterPlayer;

            List<ITerrain> terrain = gameContext.TableState.Terrain.Objects.ToList();
            List<DataBinding<UnitData>> candidates = new List<DataBinding<UnitData>>();

            IEnumerable<DataBinding<UnitData>> allUnits = gameContext.GameDataStore().GetAllValues<ArmyData>()
                .SelectMany(a => a.UnitBindings)
                .Where(u => u.GetValue().GetIsAlive() && u.GetValue().GetIsOnBattlefield());

            foreach (DataBinding<UnitData> unit in allUnits)
            {
                if (!MatchesAffinity(selector.TargetAffinity, caster, unit, IsFriendly)) continue;
                if (!WithinRangeAndSight(gameContext, measureFrom, unit, selector, terrain)) continue;
                candidates.Add(unit);
            }
            return candidates;
        }

        public static bool HasAnyEligibleTarget(IGameContext gameContext, DataBinding<UnitData> caster,
            PlayerID casterPlayer, TargetSelector selector, IUnit? origin = null)
            => GetEligibleTargets(gameContext, caster, casterPlayer, selector, origin).Count > 0;

        /// <summary>
        /// Whether ANY of <paramref name="origins"/> reaches a legal target — the castability test once a
        /// relay may be in play, since a spell the caster cannot reach itself is still castable through a
        /// conduit that can.
        /// </summary>
        public static bool HasAnyEligibleTargetFromAny(IGameContext gameContext,
            DataBinding<UnitData> caster, PlayerID casterPlayer, TargetSelector selector,
            IReadOnlyList<SpellRelay.CastOrigin> origins)
        {
            foreach (SpellRelay.CastOrigin origin in origins)
            {
                if (HasAnyEligibleTarget(gameContext, caster, casterPlayer, selector, origin.Unit))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the unit can cast. Shared by <see cref="ChooseActionStage"/> (gate the Cast action) and
        /// <see cref="CastSpellStage"/> (find friendly/enemy Casters that may modify a cast — #103).
        ///
        /// <para>Asks the rule graph for the CAPABILITY rather than testing for the <c>Caster</c> rule by
        /// identity: casting is conferred by more than one rule (<c>Caster Group</c>, whose X is a live model
        /// count and so can never be a granted <c>Caster</c>), and an identity check cannot express a
        /// capability that depends on live state. The joined-hero corner (#093 — the #006 hero-merge
        /// relocates a hero's Caster onto its model) is handled inside the query, which names the unit's
        /// models as participants exactly as the round-start token grant does.</para>
        /// </summary>
        public static bool IsCaster(IGameContext gameContext, IUnit unit) =>
            CapabilityRuleQueries.CanCast(unit, gameContext.RuleEvaluator);

        private static bool MatchesAffinity(ETargetAffinity affinity, DataBinding<UnitData> caster,
            DataBinding<UnitData> candidate, System.Func<PlayerID, bool> isFriendly)
        {
            bool friendly = isFriendly(candidate.GetValue().PlayerID);
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

        // Measured from ORIGIN, which is the caster itself unless a Spell Conduit is relaying the cast
        // (#197 P23) — "may cast spells as if they were in this model's position". Range and line of sight
        // both move with it, including which models are discounted as blockers: sighting from the relay
        // means the relay's own models are the ones that cannot block.
        private static bool WithinRangeAndSight(IGameContext gameContext, IUnit origin,
            DataBinding<UnitData> target, TargetSelector selector, IReadOnlyList<ITerrain> terrain)
        {
            IReadOnlyList<ITerrain> blockers = selector.RequireLineOfSight
                ? terrain.Concat(LineOfSightUtilities.BuildModelBlockers(gameContext.TableState, origin,
                    (IUnit)target.GetValue())).ToList()
                : terrain;

            foreach (IModel om in origin.Models)
            {
                if (!om.GetIsAlive()) continue;
                Position originPos = om.Position;
                foreach (DataBinding<ModelData> targetModel in target.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()))
                {
                    ModelData tm = targetModel.GetValue();
                    Position targetPos = tm.PositionBinding.GetValue();
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                        originPos, targetPos, om.BaseShape, om.Facing, tm.BaseShape, tm.Facing);
                    if (distance > selector.RangeInches) continue;
                    if (!selector.RequireLineOfSight) return true;
                    if (LineOfSightUtilities.HasLineOfSight(originPos, targetPos, blockers)) return true;
                }
            }
            return false;
        }
    }
}
