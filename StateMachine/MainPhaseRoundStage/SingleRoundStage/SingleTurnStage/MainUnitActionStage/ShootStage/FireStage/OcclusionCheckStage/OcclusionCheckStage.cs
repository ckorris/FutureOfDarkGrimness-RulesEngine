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

        protected override async Task RunStage(ICombatMetadata metaData, Action<OcclusionCheckResults> onFinished)
        {
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects.ToList();

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
                OnOccluded.Activate(metaData);
                return;
            }

            onFinished(new OcclusionCheckResults(isOccluded: false));
        }
    }
}
