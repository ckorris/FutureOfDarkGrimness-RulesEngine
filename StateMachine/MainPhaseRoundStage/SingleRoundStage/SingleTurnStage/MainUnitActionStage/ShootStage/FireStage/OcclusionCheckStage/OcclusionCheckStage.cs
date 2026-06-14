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
            // #042 Indirect/Takedown: a weapon that ignores intervening terrain for LoS may fire at a
            // target out of line of sight, so the occlusion gate must not cancel the shot. Same shared
            // query the enumeration and cover stages use, so they agree.
            if (Rules.Dispatch.SightRuleQueries.IgnoresTerrain(
                    metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator))
            {
                await onFinished(new OcclusionCheckResults(isOccluded: false));
                return;
            }

            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                GameContext.TableState, metaData.AttackingUnit, metaData.DefendingUnit);
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects
                .Concat(modelBlockers).ToList();

            bool hasLoS = false;
            foreach (DataBinding<ModelData> attacker in metaData.AttackingUnit.ModelBindings())
            {
                foreach (DataBinding<ModelData> defender in metaData.DefendingUnit.ModelBindings())
                {
                    if (LineOfSightUtilities.HasLineOfSight(
                        attacker.GetValue().PositionBinding.GetValue(),
                        defender.GetValue().PositionBinding.GetValue(),
                        terrain))
                    {
                        hasLoS = true;
                        break;
                    }
                }
                if (hasLoS) break;
            }

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
