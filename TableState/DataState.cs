using FDG.Data;


namespace FDG
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

        private void ObjectCreated(DataReference _, TDataObject dataObject)
        {
            OnObjectCreated?.Invoke(dataObject);
        }

        private void ObjectRemoved(DataReference _, TDataObject dataObject)
        {
            OnObjectRemoved?.Invoke(dataObject);
        }


    }
}
