using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FDG.FDGEntity;

namespace FDG
{
    public class StageHandlerRegistry
    {
        //I'm noting that this pattern is quite close to Entity's component registry.
        //That should be fine but it does irk me.
        //Also nothing makes these a handler. So it's just a dictionary of objects by their type.
        //But this is a side project, so I want to test if the way it irks me actually matters.
        private Dictionary<Type, object> _handlersByType = new Dictionary<Type, object>();

        public StageHandlerRegistry RegisterHandle<T>(T handler) where T : class
        {
            Type handlerType = typeof(T);
            AssertHandlerTypeNotYetAdded(handlerType);

            _handlersByType.Add(handlerType, handler);

            return this;
        }

        public bool ValidateAllHandlersRegistered(IEnumerable<Type> handlers, out List<Type> missingHandlers)
        {
            missingHandlers = new List<Type>();

            foreach(Type handlerType in _handlersByType.Values)
            {
                if(_handlersByType.ContainsKey(handlerType) == false)
                {
                    missingHandlers.Add(handlerType);
                }
            }

            return missingHandlers.Count == 0;
        }

        public T GetHandlerOfType<T>() where T : class
        {
            Type handlerType = typeof(T);
            if(_handlersByType.ContainsKey(handlerType) == false)
            {
                throw new MissingHandlerException($"Requested handler of type {handlerType}, but it wasn't registered. " + 
                    $"Check for missing handlers with {nameof(ValidateAllHandlersRegistered)}.");
            }

            return (T)_handlersByType[handlerType];
        }


        private void AssertHandlerTypeNotYetAdded(Type handlerType) 
        {
            if (_handlersByType.ContainsKey(handlerType))
            {
                throw new HandlerAlreadyAddedException($"Tried to register handler of type {handlerType} to {nameof(StageHandlerRegistry)}, " + 
                    "but it already had one.");
            }
        }

        public class MissingHandlerException : Exception
        {
            public MissingHandlerException(string message)
                : base(message) { }
        }

        public class HandlerAlreadyAddedException : Exception
        {
            public HandlerAlreadyAddedException(string message)
                : base(message) { }
        }
    }
}
