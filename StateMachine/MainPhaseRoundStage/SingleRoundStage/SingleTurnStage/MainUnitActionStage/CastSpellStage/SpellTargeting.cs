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
        public static List<DataBinding<UnitData>> GetEligibleTargets(IGameContext gameContext,
            DataBinding<UnitData> caster, PlayerID casterPlayer, TargetSelector selector)
        {
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
                if (!WithinRangeAndSight(gameContext, caster, unit, selector, terrain)) continue;
                candidates.Add(unit);
            }
            return candidates;
        }

        public static bool HasAnyEligibleTarget(IGameContext gameContext, DataBinding<UnitData> caster,
            PlayerID casterPlayer, TargetSelector selector)
            => GetEligibleTargets(gameContext, caster, casterPlayer, selector).Count > 0;

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
            CastingRuleQueries.CanCast(unit, gameContext.RuleEvaluator);

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

        private static bool WithinRangeAndSight(IGameContext gameContext, DataBinding<UnitData> caster,
            DataBinding<UnitData> target, TargetSelector selector, IReadOnlyList<ITerrain> terrain)
        {
            IReadOnlyList<ITerrain> blockers = selector.RequireLineOfSight
                ? terrain.Concat(LineOfSightUtilities.BuildModelBlockers(gameContext.TableState, caster, target)).ToList()
                : terrain;

            foreach (DataBinding<ModelData> casterModel in caster.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()))
            {
                ModelData cm = casterModel.GetValue();
                Position casterPos = cm.PositionBinding.GetValue();
                foreach (DataBinding<ModelData> targetModel in target.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()))
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
