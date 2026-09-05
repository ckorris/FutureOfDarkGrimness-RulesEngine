using FDG.SaveLoad;

namespace FDG.Data
{
    /// <summary>
    /// A game state the search can hold onto and resume copies of (#394): every tree node keeps one,
    /// every simulation materializes its own store from one. The two implementations are the
    /// serializer round trip the search used to run on (<see cref="JsonSnapshot"/>) and the typed
    /// in-memory copy that replaced it (<see cref="StoreSnapshot"/>); they are interchangeable by
    /// contract, and the search never cares which it has.
    /// </summary>
    public interface IStoreSnapshot
    {
        /// <summary>A fresh, independent store of this state - the caller's to mutate.</summary>
        GameDataStore Materialize();

        /// <summary>This state as a save string, for equality pins, dumps and hashes.</summary>
        string ToJson();
    }

    /// <summary>
    /// #394: a frozen typed copy of a store (<see cref="StoreClone"/>), taken once at capture and
    /// cloned again per <see cref="Materialize"/>. Nothing ever mutates the frozen copy, so any number
    /// of workers may materialize from it concurrently - the clone only reads its source.
    /// </summary>
    public sealed class StoreSnapshot : IStoreSnapshot
    {
        private readonly GameDataStore _frozen;
        private string? _json;

        private StoreSnapshot(GameDataStore frozen) => _frozen = frozen;

        /// <summary>Freezes a copy of a live store at this instant; the live store plays on untouched.</summary>
        public static StoreSnapshot Capture(GameDataStore live) => new StoreSnapshot(StoreClone.Clone(live));

        public GameDataStore Materialize() => StoreClone.Clone(_frozen);

        /// <summary>Serialized on first use only - the search never asks; tests and dumps do.</summary>
        public string ToJson() => _json ??= GameSaveSerializer.Save(_frozen);
    }

    /// <summary>The serializer path: a save string, loaded afresh per <see cref="Materialize"/>.</summary>
    public sealed class JsonSnapshot : IStoreSnapshot
    {
        private readonly string _json;

        public JsonSnapshot(string json) => _json = json;

        public GameDataStore Materialize() => GameSaveSerializer.Load(_json);

        public string ToJson() => _json;
    }
}
