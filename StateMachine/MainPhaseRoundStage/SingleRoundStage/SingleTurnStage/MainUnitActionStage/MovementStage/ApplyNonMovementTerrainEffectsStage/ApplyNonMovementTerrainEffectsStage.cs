using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
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
            float woundsBefore = movingUnit.GetValue().RemainingWounds;

            IReadOnlyList<MovementExecutor.DangerousTerrainRoll> dangerRolls =
                MovementExecutor.ApplyDangerousTerrainEffects(GameContext, paths, context.RelevantTerrain, unitName);
            foreach (MovementExecutor.DangerousTerrainRoll dt in dangerRolls)
            {
                // A single d6: 2+ is safe (green), a 1 is a wound (red).
                float[] faces = new float[6];
                faces[dt.Roll - 1] = 1f;
                await GameContext.Presenter.Present(new DiceRolledBeat(faces, sideMin: 1, successThreshold: 2,
                    GameContext.Settings.RandomnessType, "Dangerous Terrain", dt.Wounded ? "1 wound!" : "Safe"));
            }

            // A unit reduced to half strength or less by dangerous terrain takes a morale test; being at
            // half strength, a failed test Routs it. Same wound-driven path as shooting (#009).
            bool? moraleResult = await MoraleUtilities.ResolveWoundDrivenMorale(GameContext, movingUnit, woundsBefore);
            if (moraleResult == true)
            {
                GameContext.Log($"{unitName} was reduced to half strength by dangerous terrain but passed its morale test.");
            }
            else if (moraleResult == false)
            {
                GameContext.Log($"{unitName} was reduced to half strength by dangerous terrain, failed its morale test, and is Routed.");
            }

            await OnAppliedNonMovementTerrainEffects.Activate(context);
        }
    }
}
