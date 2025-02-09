
namespace FDG.Data
{
    public interface IReadableGameDataStore
    {
        T GetValue<T>(DataReference reference);

        string GetValueAsJson<T>(DataReference reference);

        IEnumerable<T> GetAllValues<T>();

        IEnumerable<DataReference> GetAllDataReferences<T>();

        event Action<DataReference, Type, object> OnDataAddedUntyped;
        event Action<DataReference, Type, object> OnDataUpdatedUntyped;
        event Action<DataReference, Type, object> OnDataRemovedUntyped;

        bool IsValid(DataReference reference, out EInvalidReason failReason);

        void SubscribeToOnCreated<T>(Action<DataReference, T> onCreated);

        void UnsubscribeFromOnCreated<T>(Action<DataReference, T> onCreated);

        void SubscribeToOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated);

        void UnsubscribeFromOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated);

        void SubscribeToOnRemoved<T>(Action<DataReference, T> onRemoved);

        void UnsubscribeFromOnRemoved<T>(Action<DataReference, T> onRemoved);
    }
}