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

            // D3+2 gives 3–5 objectives. Roll a real, decisive D3 (RollDecisive resolves one concrete
            // face even under the probabilistic roller; a plain Roll would spread to an expected value).
            // Rolling a genuine 3-sided die — rather than a d6 then halving — lets the front-end show a
            // D3 and keeps the "+2" legible. #090.
            IDiceResults rollResult = context.GameContext.DiceRoller.RollDecisive(3);
            int roll = rollResult.SideMin;
            for (int v = rollResult.SideMin; v <= rollResult.SideMax; v++)
                if (rollResult.At(v) > 0f) { roll = v; break; }
            int objectiveCount = roll + 2;

            context.Log($"Rolled D3={roll} (+2) - {objectiveCount} objectives will be placed.");

            // Show the D3 (the rolled face lights up), then a big banner with the resulting count. The
            // label carries the formula so the +2 between the rolled face and the count is understood.
            await context.GameContext.Presenter.Present(
                DiceRolledBeat.From(rollResult, roll, GameContext.Settings.RandomnessType, "Roll for Objectives (D3 + 2)",
                    $"{objectiveCount} objectives"));
            await context.Announce($"{objectiveCount} Objectives", new TextColor(255, 210, 80, 255));

            context.SetObjectiveCount(objectiveCount);
            await OnRollComplete.Activate(context);
        }
    }
}
