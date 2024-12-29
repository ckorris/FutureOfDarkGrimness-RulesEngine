
namespace FDG.Data
{
    public interface IReadableGameDataStore
    {
        T GetValue<T>(DataReference reference);

        bool IsValid(DataReference reference, out EInvalidReason failReason);
    }
}