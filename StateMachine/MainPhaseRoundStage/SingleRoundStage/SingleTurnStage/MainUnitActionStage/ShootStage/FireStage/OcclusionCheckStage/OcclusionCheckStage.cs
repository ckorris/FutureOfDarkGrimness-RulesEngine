using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    public class OcclusionCheckStage : CombatStage<OcclusionCheckResults, OcclusionCheckStage, ICombatMetadata>
    {
        public StageBinding OnOccluded;

        public OcclusionCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
            OnOccluded = new StageBinding(this);
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<OcclusionCheckResults, Task> onFinished)
        {
            // #042 Indirect: a weapon that ignores intervening terrain for LoS may fire at a
            // target out of line of sight, so the occlusion gate must not cancel the shot. Same shared
            // query the enumeration and cover stages use, so they agree. (Takedown is NOT such a weapon -
            // #314; it re-scopes the attack to one model and is occluded like any other shot.)
            if (Rules.Dispatch.SightRuleQueries.IgnoresTerrain(
                    metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator))
            {
                await onFinished(new OcclusionCheckResults(isOccluded: false));
                return;
            }

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                GameContext.TableState, metaData.AttackingUnit, metaData.DefendingUnit,
                GameContext.Settings.SeeThroughFriendlyUnits);
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects
                .Concat(modelBlockers).ToList();

            // #385: the shared unit-sees-unit gate (living, placed models on both sides) - the same
            // per-model sight test the targeting previews use, so this stage can never cancel a shot
            // that was legally offered, nor let a dead model's sight line keep one alive.
            bool hasLoS = ShotEligibility.UnitSeesUnit(
                metaData.AttackingUnit, metaData.DefendingUnit, terrain);

            if (!hasLoS)
            {
                GameContext.Log($"ERROR: No line of sight from {metaData.AttackingUnit.GetValue().Name} " +
                    $"to {metaData.DefendingUnit.GetValue().Name}. Shot cancelled.");
                await OnOccluded.Activate(metaData);
                return;
            }

            await onFinished(new OcclusionCheckResults(isOccluded: false));
        }
    }
}
