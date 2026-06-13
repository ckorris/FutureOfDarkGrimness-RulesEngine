using FDG.Stages;
using System.Collections.Generic;

namespace FDG.Samples
{
    public class TestParentStage<TChildContext> : ParentStage<IGameContext, TChildContext>
    {
        public TestParentStage(IGameContext gameContext)
            : base(gameContext, null)
        {
            
        }

        public void AssignTestObjects(StageBase<TChildContext> child,
            TChildContext childContext, StageBase<TChildContext>.StageBinding childExitBinding)
        {
            /*
            _child = child;
            _childContext = childContext;
            _childExitBinding = childExitBinding;

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(_child)
                .AddChild(new EmptyEndStage<TChildContext>(GameContext, this), out var emptyEnd)
                .Build();

            
            startingChild = _child;

            _childExitBinding.Bind(emptyEnd);

            return dictionary;
            */
        }

        protected override TChildContext GetNewChildContext(IGameContext contextSelf)
        {
            return default; //Shouldn't be used.
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<TChildContext> startingChild)
        {
            //Use AssignTestObjects instead.
            startingChild = null;
            return null;
        }
    }
}
