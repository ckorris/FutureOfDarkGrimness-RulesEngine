
namespace FDG.Data
{
    public interface IReadWriteableGameDataStore : IReadableGameDataStore
    {
        void SetValue<T>(DataReference reference, T value);

        void SetValueWithJson(DataReference reference, string json);

        DataReference Create<T>(T initialValue);

        bool Destroy(DataReference reference);

        DataBinding<T> GetDataBinding<T>(DataReference dataReference);
    }
}