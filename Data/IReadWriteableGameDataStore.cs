
namespace FDG.Data
{
    public interface IReadWriteableGameDataStore : IReadableGameDataStore
    {
        void SetValue<T>(DataReference reference, T value);

        DataReference Create<T>();

        bool Destroy(DataReference reference);
    }
}