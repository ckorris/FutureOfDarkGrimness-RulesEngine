namespace FDG.Stages
{
    public class RollForObjectiveCountStage : StageBase<IGameContext>
    {
        public StageBinding OnRollComplete;

        private readonly Action<int>? _onCountDetermined;

        public RollForObjectiveCountStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent,
            Action<int>? onCountDetermined = null)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
            _onCountDetermined = onCountDetermined;
        }

        public override async Task Enter(IGameContext context)
        {
            context.Log($"Entered {nameof(RollForObjectiveCountStage)}.");

            // D3+2 gives 3–5 objectives. Roll a d6; sides are 1-indexed.
            IDiceResults rollResult = context.DiceRoller.Roll(1);
            int roll = rollResult.SideMin;
            for (int v = rollResult.SideMin; v <= rollResult.SideMax; v++)
                if (rollResult.At(v) > 0f) { roll = v; break; }
            int d3 = (roll + 1) / 2; // 1→1, 2–3→2, 4–6→3
            int objectiveCount = d3 + 2;

            context.Log($"Rolled {roll} — {objectiveCount} objectives will be placed.");

            _onCountDetermined?.Invoke(objectiveCount);
            OnRollComplete.Activate(context);
        }
    }
}
