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

            // Flying (AllTerrain scope) ignores Dangerous-terrain effects — no rolls, no wounds.
            bool ignoresDangerousTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                movingUnit.GetValue(), GameContext.RuleEvaluator);

            // "Counts as being in Dangerous Terrain" (#153): every moving model tests this move. Read-only
            // query — the one-shot grant is spent later by ExecuteMoveStage, after this stage has read it.
            bool countsAsInDangerousTerrain = Rules.Dispatch.MovementRuleQueries.CountsAsInTerrain(
                movingUnit.GetValue(), GameContext.RuleEvaluator, Rules.Definitions.ECountAsTerrain.Dangerous);

            // Roll here, land it later. The dice draw stays in its original place in the seeded stream,
            // but the wounds are held pending and applied by ExecuteMoveStage once the move has been
            // animated — a model killed crossing dangerous terrain should fall at the far side, not wink
            // out on the start line and then have the rest of its unit walk off without it.
            MovementExecutor.DangerousTerrainResult dangerResult =
                MovementExecutor.RollDangerousTerrain(GameContext, paths, context.RelevantTerrain,
                    movingUnit.GetValue(), ignoresDangerousTerrain, countsAsInDangerousTerrain);

            context.RegisterDangerousTerrainRoll(dangerResult);

            await OnAppliedNonMovementTerrainEffects.Activate(context);
        }
    }
}
