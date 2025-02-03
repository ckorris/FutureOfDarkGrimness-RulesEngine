using FDG.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FutureOfDarkGrimness.TableState
{

    public interface IDataState<TInterface>
    {
        public event Action<TInterface> OnObjectCreated;

        public event Action<TInterface> OnObjectRemoved;

        public IEnumerable<TInterface> Objects { get; }
    }

    internal class DataState<TInterface, TDataObject> : IDataState<TInterface>
        where TDataObject : TInterface
    {

        public event Action<TInterface>? OnObjectCreated;

        public event Action<TInterface>? OnObjectRemoved;


        private IReadableGameDataStore _gameDataStore;

        public IEnumerable<TInterface> Objects => _gameDataStore.GetAllValues<TDataObject>()
            .Cast<TInterface>();

        internal DataState(IReadableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
            _gameDataStore.SubscribeToOnCreated<TDataObject>(ObjectCreated);
            _gameDataStore.SubscribeToOnRemoved<TDataObject>(ObjectRemoved);
        }

        private void ObjectCreated(TDataObject dataObject)
        {
            OnObjectCreated?.Invoke(dataObject);
        }

        private void ObjectRemoved(TDataObject dataObject)
        {
            OnObjectRemoved?.Invoke(dataObject);
        }


    }
}
