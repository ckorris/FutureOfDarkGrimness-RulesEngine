namespace FDG.Stages
{
    public class RollForObjectiveCountStage : StageBase<IGameContext>
    {
        public StageBinding OnRollComplete;

        public RollForObjectiveCountStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(RollForObjectiveCountStage)}.");

            // D3+2 gives 3–5 objectives, a common scenario spread.
            int roll = (int)context.DiceRoller.Roll(1)[1];
            int d3 = (roll + 1) / 2; // 1→1, 2–3→2, 4–6→3
            int objectiveCount = d3 + 2;

            context.Log($"Rolled {roll} — {objectiveCount} objectives will be placed.");

            OnRollComplete.Activate(context);
        }
    }
}
