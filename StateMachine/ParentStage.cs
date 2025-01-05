
namespace FDG.Stages
{
    public abstract class ParentStage<TContextSelf, TContextChild>
        : StageBase<TContextSelf>, IStateMachineLayer<TContextChild>
    {
        protected delegate void Transition(TContextChild childContext);

        private Dictionary<string, Transition> _transitions;

        public StageBase<TContextChild> CurrentChild { get; private set; }

        private StageBase<TContextChild> _startingChild;

        protected ParentStage(IGameContext gameContext, IStateMachineLayer<TContextSelf> parent)
            : base(gameContext, parent)
        {
            _transitions = PopulateTransitions(out _startingChild);
        }

        protected abstract Dictionary<string, Transition> PopulateTransitions(out StageBase<TContextChild> startingChild);

        protected abstract TContextChild GetNewChildContext(TContextSelf contextSelf);

        private TContextSelf _currentContext = default;
        private bool _hasContext = false;

        public override void Enter(TContextSelf context)
        {
            _currentContext = context;
            _hasContext = true;

            TContextChild childContext = GetNewChildContext(context);

            TransitionToChild(_startingChild, childContext);
        }

        public override void Exit()
        {
            if (CurrentChild != null)
            {
                Parent?.NotifyChildExited(CurrentChild);
                CurrentChild = null;
            }

            _currentContext = default;
            _hasContext = false;
        }

        public void ExecuteTransition(string eventName, StageBase<TContextChild> leavingChild, TContextChild childContext)
        {
            if (_transitions.TryGetValue(eventName, out Transition transition) == false)
            {
                throw new KeyNotFoundException();
            }

            leavingChild.Exit();
            Parent?.NotifyChildExited(leavingChild);

            transition.Invoke(childContext);
        }

        private void TransitionToChild(StageBase<TContextChild> newChild, TContextChild childContext)
        {
            CurrentChild = newChild;

            newChild.Enter(childContext);
            Parent?.NotifyChildExited(newChild);
        }

        private void TransitionToSibling(StageBinding siblingBinding)
        {
            if (_hasContext == false)
            {
                throw new InvalidOperationException($"Tried to call {nameof(TransitionToSibling)} when no context was assigned.");
            }

            siblingBinding.Activate(_currentContext);
        }

        public void NotifyChildEntered(IStage enteredStage)
        {
            //Send it up the way until something cares.
            if (Parent != null)
            {
                Parent.NotifyChildEntered(enteredStage);
            }
        }

        public void NotifyChildExited(IStage enteredStage)
        {
            if (Parent != null)
            {
                Parent.NotifyChildExited(enteredStage);
            }
        }

        protected class TransitionSetBuilder
        {
            private Dictionary<string, Transition> _dictionary = new Dictionary<string, Transition>();

            private ParentStage<TContextSelf, TContextChild> _parentStage;

            public TransitionSetBuilder(ParentStage<TContextSelf, TContextChild> parentStage)
            {
                _parentStage = parentStage;
            }

            public TransitionSetBuilder AddChild(StageBase<TContextChild> stage)
            {
                _dictionary.Add(stage.Name, (context) => _parentStage.TransitionToChild(stage, context));
                return this;
            }

            public TransitionSetBuilder AddChild<TChild>(TChild stageIn, out TChild stageOut) //For declaration options.
                where TChild : StageBase<TContextChild>
            {
                stageOut = stageIn;
                return AddChild(stageIn);
            }

            public TransitionSetBuilder AddSibling(string eventName, StageBinding siblingBinding)
            {
                if (siblingBinding == null)
                {
                    throw new System.NullReferenceException();
                }

                _dictionary.Add(eventName, (context) => _parentStage.TransitionToSibling(siblingBinding));
                return this;
            }

            public TransitionSetBuilder AddSibling(string eventNameIn, StageBinding siblingBinding, out string eventNameOut)
            {
                eventNameOut = eventNameIn;
                return AddSibling(eventNameIn, siblingBinding);
            }


            public Dictionary<string, Transition> Build()
            {
                return _dictionary;
            }
        }
    }
}
