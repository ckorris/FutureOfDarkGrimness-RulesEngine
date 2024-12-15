

using System.Collections.Generic;

namespace FDG.Stages
{
    public class EmptyParent<TChildContext> : IStateMachineLayer<TChildContext>
    {
        public void ExecuteTransition(string eventName, StageBase<TChildContext> leavingChild, TChildContext childContext)
        {
            //Purposefully do nothing.
        }
    }


    /*
    public class EmptyParent<TChildContext> : ParentStage<IEmptyParentContext, TChildContext>
    {
        private TChildContext _childContext;
        private List<StageBase<TChildContext>> _children;

        public EmptyParent(IGameContext gameContext, IStateMachineLayer<IEmptyParentContext> parent,
            TChildContext childContext, List<StageBase<TChildContext>> children) : base(gameContext, parent)
        {
            _childContext = childContext;
            _children = children;
        }

        protected override TChildContext GetNewChildContext(IEmptyParentContext contextSelf)
        {
            return _childContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<TChildContext> startingChild)
        {
            startingChild = null;

            TransitionSetBuilder builder = new TransitionSetBuilder(this);

            foreach (StageBase<TChildContext> child in _children)
            {
                builder.AddChild(child);
            }

            return builder.Build();
        }
    */

    public interface IEmptyParentContext
    {

    }
}