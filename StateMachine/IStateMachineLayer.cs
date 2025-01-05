
namespace FDG.Stages
{
    public interface IStateMachineLayer<TContext>
    {
        public void ExecuteTransition(string eventName, StageBase<TContext> leavingChild, TContext childContext);

        public void NotifyChildEntered(IStage enteredStage);

        public void NotifyChildExited(IStage enteredStage);
    }
}
