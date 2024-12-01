
using System.Collections.Generic;

namespace FDG.Stages
{
    public abstract class ParentStage<TContextSelf, TContextChild> 
        : StageBase<TContextSelf>, IStateMachineLayer<TContextChild>
    {
        private Dictionary<string, StageBase<TContextChild>> _children;

        public string CurrentChild { get; private set; }

        protected ParentStage(IGameContext gameContext, IStateMachineLayer<TContextSelf> parent) 
            : base(gameContext, parent)
        {
            PopulateChildren();
        }

        protected abstract Dictionary<string, StageBase<TContextChild>> PopulateChildren();

        protected abstract string GetStartingChildName();

        protected abstract TContextChild GetNewChildContext();

        public override void Enter(TContextSelf context)
        {
            string firstEvent = GetStartingChildName();

            TContextChild childContext = GetNewChildContext();

            ProcessEvent(firstEvent, childContext);
        }

        public override void Exit()
        {
            if(CurrentChild != null)
            {
                _children[CurrentChild].Exit();
                CurrentChild = null;
            }
        }

        public void ProcessEvent(string nextStageName, TContextChild childContext)
        {
            if (CurrentChild != null)
            {
                _children[CurrentChild].Exit();
            }

            if (_children.ContainsKey(nextStageName) == false)
            {
                throw new KeyNotFoundException($"No child with the key {nextStageName} exists in stage {GetType().Name}.");
            }

            CurrentChild = nextStageName;
            _children[nextStageName].Enter(childContext);
        }

        protected class ChildDictionaryBuilder()
        {
            private Dictionary<string, StageBase<TContextChild>> _dictionary = new Dictionary<string, StageBase<TContextChild>>();

            public ChildDictionaryBuilder Add(StageBase<TContextChild> stage)
            {
                _dictionary.Add(stage.Name, stage);
                return this;
            }

            public Dictionary<string, StageBase<TContextChild>> Build()
            {
                return _dictionary;
            }
        }
    }
}
