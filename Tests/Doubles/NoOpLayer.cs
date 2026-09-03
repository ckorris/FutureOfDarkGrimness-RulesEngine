using FDG.Stages;

namespace FDG.Tests
{
    // Parent layer that accepts all transitions without doing anything. Public: reused as the app-side
    // book-spell-probe's parent layer too (FdgRaylib.Tests/BookSpellCastProbeTests.cs).
    public class NoOpLayer<TContext> : IStateMachineLayer<TContext>
    {
        public Task ExecuteTransition(string eventName, StageBase<TContext> leaving, TContext context)
            => Task.CompletedTask;
        public void NotifyChildEntered(IStage stage) { }
        public void NotifyChildExited(IStage stage) { }
    }
}
