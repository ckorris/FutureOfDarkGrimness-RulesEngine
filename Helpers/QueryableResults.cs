using System;
using System.Collections.Concurrent;

namespace FDG
{
    public class QueryableResults 
    {
        //Value must be of the key type.
        private ConcurrentDictionary<Type, object> _registeredResults = new ConcurrentDictionary<Type, object>();

        public void AddResult<TResult>(TResult result)
        {
            Type type = typeof(TResult);

            //There will already be an exception if the dictionary contains it.
            if(_registeredResults.TryAdd(type, result) == false)
            {
                throw new ArgumentException($"Result of type {type.Name} already exists in {nameof(QueryableResults)}.");
            }
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            Type type = typeof(TResult);

            object untypedResult;

            if (_registeredResults.TryGetValue(type, out untypedResult))
            {
                result = (TResult)untypedResult;
                return true;
            }

            result = default;
            return false;
        }

        public void Reset()
        {
            _registeredResults.Clear();
        }
    }
}