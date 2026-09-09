namespace FDG.Stages
{
    /// <summary>
    /// A parent layer that accepts every transition and does nothing with it - for code that drives
    /// stages directly rather than through a running state machine (#397's <c>CombatCalculator</c>).
    /// A stage's <c>NextStage.Activate</c> ends up here, so binding it to any event name and entering
    /// the stage runs exactly that one stage and stops.
    /// <para>
    /// The test doubles have carried a <c>NoOpLayer</c> for this since the first stage tests; this is
    /// the production twin, so shipping code never has to reference <c>FDG.Tests</c>.
    /// </para>
    /// </summary>
    public sealed class PassThroughLayer<TContext> : IStateMachineLayer<TContext>
    {
        public Task ExecuteTransition(string eventName, StageBase<TContext> leavingChild, TContext childContext)
            => Task.CompletedTask;

        public void NotifyChildEntered(IStage enteredStage) { }

        public void NotifyChildExited(IStage enteredStage) { }
    }
}
