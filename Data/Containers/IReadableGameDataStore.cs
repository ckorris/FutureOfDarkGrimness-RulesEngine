
using FDG.Data.Containers;
using Newtonsoft.Json;

namespace FDG.Data
{
    public interface IReadableGameDataStore
    {
        T GetValue<T>(DataReference reference);

        string GetValueAsJson<T>(DataReference reference);

        IEnumerable<T> GetAllValues<T>();

        IEnumerable<DataReference> GetAllDataReferences<T>();

        event Action<DataReference, string> OnDataAddedAsJson;
        event Action<DataReference, string> OnDataUpdatedAsJson;
        event Action<DataReference> OnDataRemoved;

        JsonSerializerSettings GetJsonSettings();

        List<ReferenceJsonValuePair> GetAllDataReferencesAsJson();

        bool IsValid(DataReference reference, out EInvalidReason failReason);

        void SubscribeToOnCreated<T>(Action<DataReference, T> onCreated);

        void UnsubscribeFromOnCreated<T>(Action<DataReference, T> onCreated);

        void SubscribeToOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated);

        void UnsubscribeFromOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated);

        void SubscribeToOnRemoved<T>(Action<DataReference, T> onRemoved);

        void UnsubscribeFromOnRemoved<T>(Action<DataReference, T> onRemoved);
    }
}