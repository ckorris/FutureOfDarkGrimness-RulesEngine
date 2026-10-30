using FDG.Data;
using FDG.StageResolution;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FDG.Replay
{
    /// <summary>
    /// One line of a <c>.fdgreplay</c> after the header (plan 4.1). <see cref="Seq"/> is dense per recording
    /// (assigned under the recorder's lock), <see cref="T"/> is milliseconds since the recording started,
    /// and <see cref="Kind"/> is the explicit discriminator - written first after Seq/T so a line reads
    /// <c>{"Seq":12,"T":340,"Kind":"StoreUpdate",...}</c>.
    /// </summary>
    public abstract record ReplayEvent
    {
        [JsonProperty(Order = -4)]
        public long Seq { get; init; }

        [JsonProperty(Order = -3)]
        public long T { get; init; }

        [JsonProperty(Order = -2)]
        public string Kind => ReplayEventKinds.KindOf(GetType());
    }

    /// <summary>A store entry was created (<c>GameDataStore.OnDataAddedAsJson</c>). <see cref="Json"/> is the
    /// value as the store's own settings wrote it.</summary>
    public sealed record StoreAddEvent(DataReference Ref, string Json) : ReplayEvent;

    /// <summary>A store entry was rewritten (<c>OnDataUpdatedAsJson</c>): the whole new value.</summary>
    public sealed record StoreUpdateEvent(DataReference Ref, string Json) : ReplayEvent;

    /// <summary>A store entry was destroyed (<c>OnDataRemoved</c>).</summary>
    public sealed record StoreRemoveEvent(DataReference Ref) : ReplayEvent;

    /// <summary>
    /// A full store snapshot - a seek point (plan 4.1). <see cref="SaveJson"/> is a complete
    /// <c>GameSaveSerializer.Save</c> of the store at that moment. <see cref="Index"/> counts snapshots in
    /// this recording from 0 and is what <c>ReplayMarkerMessage</c> carries to clients (never the JSON).
    /// </summary>
    public sealed record SnapshotEvent(int Index, string SaveJson) : ReplayEvent;

    /// <summary>A decision was requested. <see cref="RequestJson"/> is the body exactly as it went on the wire
    /// (<c>WireJsonSettings</c>, <c>$type</c>-tagged); the type names are full CLR names.</summary>
    public sealed record RequestEvent(TaskID TaskId, PlayerID PlayerId, string RequestType, string ReplyType,
        string DisplayName, string RequestJson) : ReplayEvent;

    /// <summary>The decision came back. <see cref="ReplyJson"/> is the body exactly as received.</summary>
    public sealed record ReplyEvent(TaskID TaskId, string ReplyJson) : ReplayEvent;

    /// <summary>The request failed instead of being answered (a resolver error reply).</summary>
    public sealed record RequestErrorEvent(TaskID TaskId, string Message) : ReplayEvent;

    /// <summary>A presentation beat, <c>$type</c>-tagged with the wire settings <c>PresentBeatMessage</c>
    /// uses, so it deserializes back to its concrete <c>PresentationBeat</c> subclass.</summary>
    public sealed record BeatEvent(string BeatJson) : ReplayEvent;

    /// <summary>A game-log line (the stages' <c>ITextOutput</c>). <see cref="Debug"/> marks the Debug-category
    /// lines the front end hides unless its Debug toggle is on; omitted from the file when false.</summary>
    public sealed record LogEvent(string Line) : ReplayEvent
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Debug { get; init; }
    }

    /// <summary>Which flow boundary a <see cref="MarkerEvent"/> records.</summary>
    public enum EReplayMarker
    {
        /// <summary>The recording begins. Fresh game: right after world creation, before map setup
        /// (<see cref="MarkerEvent.Round"/> null). Resume / scenario launch: the round the game resumes in.
        /// Always followed by snapshot 0.</summary>
        GameStart,

        /// <summary>A main-phase round begins (before its round banner and start-of-round arrivals).
        /// Always followed by a snapshot.</summary>
        RoundStart,

        /// <summary>
        /// An activation begins. Emitted at the activation boundary with <see cref="MarkerEvent.UnitRef"/>
        /// null (the acting player is known, the unit is not yet chosen), then REPEATED with the same
        /// Round/Activation once the unit is committed, carrying its UnitRef - and again after a backed-out
        /// activation (#248) re-picks. A repeat is not a new activation. A turn spent on Delayed Action has
        /// no repeat (no unit activated).
        /// </summary>
        ActivationStart,

        /// <summary>The game was saved (#453).</summary>
        Saved,

        /// <summary>A hard boundary: the game was reloaded; the following snapshot replaces state (#453).</summary>
        Resumed,

        /// <summary>The game finished, however it finished (<see cref="MarkerEvent.Outcome"/> /
        /// <see cref="MarkerEvent.Message"/>).</summary>
        GameEnded,
    }

    /// <summary>A flow boundary. Which optional fields are set depends on <see cref="Marker"/>.</summary>
    public sealed record MarkerEvent(EReplayMarker Marker) : ReplayEvent
    {
        public int? Round { get; init; }

        /// <summary>1-based turn index within <see cref="Round"/>, counted by the recording side since the
        /// last RoundStart (or since GameStart on a resumed recording).</summary>
        public int? Activation { get; init; }

        public PlayerID? PlayerId { get; init; }

        public DataReference? UnitRef { get; init; }

        /// <summary>GameEnded: the <c>EGameOutcome</c> name (Win / Tie / Fault / Disconnect).</summary>
        public string? Outcome { get; init; }

        /// <summary>GameEnded: the player-facing end-of-game sentence.</summary>
        public string? Message { get; init; }
    }

    /// <summary>The fixed Kind &lt;-&gt; type table. The reader resolves ONLY these names (no <c>$type</c>).</summary>
    internal static class ReplayEventKinds
    {
        private static readonly (string Kind, Type Type)[] Table =
        {
            ("StoreAdd", typeof(StoreAddEvent)),
            ("StoreUpdate", typeof(StoreUpdateEvent)),
            ("StoreRemove", typeof(StoreRemoveEvent)),
            ("Snapshot", typeof(SnapshotEvent)),
            ("Request", typeof(RequestEvent)),
            ("Reply", typeof(ReplyEvent)),
            ("RequestError", typeof(RequestErrorEvent)),
            ("Beat", typeof(BeatEvent)),
            ("Log", typeof(LogEvent)),
            ("Marker", typeof(MarkerEvent)),
        };

        private static readonly Dictionary<Type, string> KindByType = Table.ToDictionary(e => e.Type, e => e.Kind);

        private static readonly Dictionary<string, Type> TypeByKind = Table.ToDictionary(e => e.Kind, e => e.Type);

        public static IEnumerable<string> AllKinds => TypeByKind.Keys;

        public static string KindOf(Type eventType) =>
            KindByType.TryGetValue(eventType, out string? kind)
                ? kind
                : throw new InvalidOperationException($"{eventType.Name} has no replay Kind registered.");

        public static bool TryGetType(string kind, out Type type) => TypeByKind.TryGetValue(kind, out type!);
    }

    /// <summary>
    /// Reads a <see cref="ReplayEvent"/> line by its <c>Kind</c>. Read-only: writing goes through the
    /// default contract (the concrete record, with its computed Kind). Applies to the abstract base only,
    /// so the concrete <c>ToObject</c> inside does not recurse back into it.
    /// </summary>
    internal sealed class ReplayEventJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(ReplayEvent);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            JObject line = JObject.Load(reader);
            string? kind = (string?)line["Kind"];
            if (kind == null)
            {
                throw new ReplayFormatException("Replay event has no Kind.");
            }

            if (ReplayEventKinds.TryGetType(kind, out Type type) == false)
            {
                throw new ReplayFormatException($"Unknown replay event kind '{kind}'.");
            }

            return line.ToObject(type, serializer);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }
}
