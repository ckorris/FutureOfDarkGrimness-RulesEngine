using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{
    public abstract partial class StageBase<TContext>
    {


        public class StageBinding
        {
            private StageBase<TContext> _sourceStage;

            private string _eventName = null;

            public bool IsBound => _eventName != null;

            public StageBinding(StageBase<TContext> sourceStage)
            {
                _sourceStage = sourceStage;
            }

            public StageBase<TContext> Bind(string eventName)
            {
                if(IsBound)
                {
                    throw new InvalidOperationException($"{nameof(StageBinding)} is already bound to {_eventName}. Tried to " +
                        $"bind it to {eventName}.");
                }

                _eventName = eventName;

                return _sourceStage;
            }

            public StageBase<TContext> Bind(StageBase<TContext> transitionStage)
            {
                return Bind(transitionStage.Name);
            }

            public void Activate(TContext context)
            {
                if(_eventName == null)
                {
                    throw new ArgumentNullException($"{nameof(StageBinding)} was not bound before calling {nameof(Activate)}.");
                }

                _sourceStage.SignalEvent(_eventName, context);
            }
        }
    }
}
