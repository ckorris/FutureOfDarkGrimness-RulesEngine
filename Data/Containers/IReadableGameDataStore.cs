
namespace FDG.Data
{
    public interface IReadableGameDataStore
    {
        T GetValue<T>(DataReference reference);

        IEnumerable<T> GetAllValues<T>();

        IEnumerable<DataReference> GetAllDataReferences<T>();

        bool IsValid(DataReference reference, out EInvalidReason failReason);

        void SubscribeToOnCreated<T>(Action<T> onCreated);

        void UnsubscribeFromOnCreated<T>(Action<T> onCreated);

        void SubscribeToOnRemoved<T>(Action<T> onRemoved);

        void UnsubscribeFromOnRemoved<T>(Action<T> onRemoved);
    }
}