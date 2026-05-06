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
                OnAppliedNonMovementTerrainEffects.Activate(context);
                return;
            }

            List<ITerrain> dangerous = context.RelevantTerrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList();

            if (dangerous.Count == 0)
            {
                OnAppliedNonMovementTerrainEffects.Activate(context);
                return;
            }

            string unitName = context.MovingUnit.GetValue().Name;

            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;

                if (!MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous)) continue;

                IDiceResults roll = GameContext.DiceRoller.Roll(6, 1);

                // Die faces start at SideMin (1), not 0 — find which face came up.
                int rollValue = roll.SideMin;
                for (int v = roll.SideMin; v <= roll.SideMax; v++)
                    if (roll.At(v) > 0f) { rollValue = v; break; }

                if (roll.At(1) > 0)
                {
                    move.Model.GetValue().DealWounds(1);
                    GameContext.Log($"{unitName}: model crossed dangerous terrain, rolled {rollValue} — 1 wound dealt.");
                }
                else
                {
                    GameContext.Log($"{unitName}: model crossed dangerous terrain, rolled {rollValue} — safe.");
                }
            }

            OnAppliedNonMovementTerrainEffects.Activate(context);
        }
    }
}
