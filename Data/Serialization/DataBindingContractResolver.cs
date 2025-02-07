using Newtonsoft.Json.Serialization;

namespace FDG.Data.Serialization
{
    internal class DataBindingContractResolver : DefaultContractResolver
    {
        private readonly IReadWriteableGameDataStore _gameDataStore;

        internal DataBindingContractResolver(IReadWriteableGameDataStore gameDataStore)
        {
            _gameDataStore = gameDataStore;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            JsonObjectContract contract = base.CreateObjectContract(objectType);

            if (typeof(IGameDataAware).IsAssignableFrom(objectType))
            {
                contract.OnDeserializedCallbacks.Add((obj, context) =>
                    {
                        IGameDataAware gameDataAware = (IGameDataAware)obj;
                        gameDataAware.SetGameDataStore(_gameDataStore);
                    });
            }

            return contract;
        }


    }
}
