
namespace FDG.Stages
{
    public interface IStateMachineLayer<TContext>
    {
        public void ExecuteTransition(string eventName, StageBase<TContext> leavingChild, TContext childContext);
    }
}
