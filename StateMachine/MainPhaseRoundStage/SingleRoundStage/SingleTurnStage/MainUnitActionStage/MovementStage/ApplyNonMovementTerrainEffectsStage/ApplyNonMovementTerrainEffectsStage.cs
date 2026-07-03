using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class ApplyNonMovementTerrainEffectsStage : StageBase<IMovementActionContext>
    {
        public StageBinding OnAppliedNonMovementTerrainEffects;

        public ApplyNonMovementTerrainEffectsStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
            OnAppliedNonMovementTerrainEffects = new StageBinding(this);
        }

        public override async Task Enter(IMovementActionContext context)
        {
            if (!context.TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths))
            {
                await OnAppliedNonMovementTerrainEffects.Activate(context);
                return;
            }

            DataBinding<UnitData> movingUnit = context.MovingUnit;
            string unitName = movingUnit.GetValue().Name;

            // Flying (AllTerrain scope) ignores Dangerous-terrain effects — no rolls, no wounds.
            bool ignoresDangerousTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                movingUnit.GetValue(), GameContext.RuleEvaluator);

            IReadOnlyList<MovementExecutor.DangerousTerrainRoll> dangerRolls =
                MovementExecutor.ApplyDangerousTerrainEffects(GameContext, paths, context.RelevantTerrain, unitName,
                    ignoresDangerousTerrain);

            // Dangerous terrain deals wounds but is NOT a morale-test source — shooting, melee, transport
            // destruction, etc. trigger morale tests, but crossing dangerous terrain never does. Present the
            // roll(s) (shared with the triggered-move path) and move on; no morale test here.
            await MovementExecutor.PresentDangerousTerrainRolls(GameContext, dangerRolls);

            await OnAppliedNonMovementTerrainEffects.Activate(context);
        }
    }
}
