using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class StateMachine
    {
        private Stack<StateBase> _stateStack = new Stack<StateBase>();

        //Transition map: (CurrentStateType, EventName) -> NextStateInstance.
        private Dictionary<(Type StateType, string Event), StateBase> _transitions =
            new Dictionary<(Type, string), StateBase>();

        public void Start(StateBase initialState)
        {
            if (initialState == null)
                throw new ArgumentNullException(nameof(initialState));

            PushState(initialState);
        }

        public void PushState(StateBase state)
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

        public void ChangeState(StateBase newState)
        {
            if (newState == null)
                throw new ArgumentNullException(nameof(newState));

            PopState();
            PushState(newState);
        }

        public void AddTransition<TState>(string eventName, StateBase nextState)
            where TState : StateBase
        {
            _transitions[(typeof(TState), eventName)] = nextState;
        }

        public void AddTransition<TState>(TState oldState, string eventName, StateBase nextState)
            where TState : StateBase
        {
            _transitions[(typeof(TState), eventName)] = nextState;
        }

        //Method called by states to signal events.
        public void ProcessEvent(StateBase state, string eventName, object eventData = null)
        {
            var key = (state.GetType(), eventName);
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

        public StateBase CurrentState => _stateStack.Count > 0 ? _stateStack.Peek() : null;
    }

}