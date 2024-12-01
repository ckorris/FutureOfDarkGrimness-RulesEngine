using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    /*
    public class StateMachine
    {
        private Stack<StageBase> _stateStack = new Stack<StageBase>();

        //Transition map: (CurrentStateType, EventName) -> NextStateInstance.
        private Dictionary<(StageBase StateType, string Event), StageBase> _transitions =
            new Dictionary<(StageBase, string), StageBase>();

        public void Start(StageBase initialState)
        {
            if (initialState == null)
                throw new ArgumentNullException(nameof(initialState));

            PushState(initialState);
        }

        public void PushState(StageBase state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            _stateStack.Push(state);
            state.Enter();
        }

        public void PopState()
        {
            if (_stateStack.Count > 0)
            {
                var state = _stateStack.Pop();
                state.Exit();
            }
        }

        public void ChangeState(StageBase newState)
        {
            if (newState == null)
                throw new ArgumentNullException(nameof(newState));

            PopState();
            PushState(newState);
        }

        public void AddTransition(StageBase sourceState, string eventName, StageBase nextState)
        {
            _transitions[(sourceState, eventName)] = nextState;
        }


        //Method called by states to signal events.
        public void ProcessEvent(StageBase state, string eventName, object eventData = null)
        {
            var key = (state, eventName);
            if (_transitions.TryGetValue(key, out var nextState))
            {
                //Set context on nextState if necessary.
                if (eventData != null && nextState is IContextAware contextAwareState)
                {
                    contextAwareState.SetContext(eventData);
                }
                ChangeState(nextState);
            }
            else
            {
                throw new InvalidOperationException($"No transition defined for state {state.GetType().Name} on event '{eventName}'.");
            }
        }

        public StageBase CurrentState => _stateStack.Count > 0 ? _stateStack.Peek() : null;
    }
    */
}