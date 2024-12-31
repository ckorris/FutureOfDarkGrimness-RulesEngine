
namespace FDG.Data
{
    public interface IReadWriteableGameDataStore : IReadableGameDataStore
    {
        void SetValue<T>(DataReference reference, T value);

        DataReference Create<T>(T initialValue);

        bool Destroy(DataReference reference);
    }
}