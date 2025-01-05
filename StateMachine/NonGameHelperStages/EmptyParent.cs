
namespace FDG.Stages
{
    public class EmptyParent<TChildContext> : IStateMachineLayer<TChildContext>
    {
        public void ExecuteTransition(string eventName, StageBase<TChildContext> leavingChild, TChildContext childContext)
        {
            //Purposefully do nothing.
        }

        public void NotifyChildEntered(IStage enteredStage)
        {
            
        }

        public void NotifyChildExited(IStage enteredStage)
        {
            
        }
    }

    public interface IEmptyParentContext
    {

    }
}