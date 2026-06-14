using FDG.Presentation.Beats;

namespace FDG.Stages
{
    public class RollForObjectiveCountStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnRollComplete;

        public RollForObjectiveCountStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.Log($"Entered {nameof(RollForObjectiveCountStage)}.");

            // D3+2 gives 3–5 objectives. Roll a d6; sides are 1-indexed.
            IDiceResults rollResult = context.GameContext.DiceRoller.Roll(1);
            int roll = rollResult.SideMin;
            for (int v = rollResult.SideMin; v <= rollResult.SideMax; v++)
                if (rollResult.At(v) > 0f) { roll = v; break; }
            int d3 = (roll + 1) / 2; // 1→1, 2–3→2, 4–6→3
            int objectiveCount = d3 + 2;

            context.Log($"Rolled {roll} - {objectiveCount} objectives will be placed.");

            // Show the d6 (the rolled face lights up), then a big banner with the resulting count.
            await context.GameContext.Presenter.Present(
                DiceRolledBeat.From(rollResult, roll, GameContext.Settings.RandomnessType, "Objective Count"));
            await context.Announce($"{objectiveCount} Objectives", new TextColor(255, 210, 80, 255));

            context.SetObjectiveCount(objectiveCount);
            OnRollComplete.Activate(context);
        }
    }
}
