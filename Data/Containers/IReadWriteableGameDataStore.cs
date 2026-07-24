
namespace FDG.Data
{
    public interface IReadWriteableGameDataStore : IReadableGameDataStore
    {
        void SetValue<T>(DataReference reference, T value);

        void SetValueWithJson(DataReference reference, string json);

        DataReference Create<T>(T initialValue);

        /// <summary>Creates at a foreign reference from the live incremental stream (a network add).</summary>
        void CreateFromReferenceAndJson(DataReference reference, string initValueAsJson);

        /// <summary>
        /// Creates at a foreign reference while rebuilding this store from a whole-store snapshot (a
        /// save file, or the join-time catch-up). Adopts the entry's generation rather than requiring it
        /// to follow on from this store's - see <see cref="ComponentStore{T}.CreateFromReplay"/> (#270).
        /// </summary>
        void CreateFromReplayJson(DataReference reference, string initValueAsJson);

        bool Destroy(DataReference reference);

        DataBinding<T> GetDataBinding<T>(DataReference dataReference);

        IEnumerable<DataBinding<T>> GetAllDataBindings<T>();
    }
}