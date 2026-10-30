using FDG.Data;
using FDG.Replay;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Broadcast host -> clients (#450) for every replay marker the host records, and for every snapshot it
    /// takes, so a client's own recording (#451) carries identical markers and snapshots its own store at the
    /// same points. A snapshot cue carries only <see cref="SnapshotIndex"/>, never the save JSON - the client
    /// already holds the same store. Exactly one of <see cref="Marker"/> / <see cref="SnapshotIndex"/> is set.
    /// <para>The host does not handle it; it exists for the clients, like <see cref="GameEndedMessage"/>.</para>
    /// </summary>
    internal class ReplayMarkerMessage
    {
        public EReplayMarker? Marker { get; set; }

        public int? Round { get; set; }

        public int? Activation { get; set; }

        public PlayerID? PlayerId { get; set; }

        public DataReference? UnitRef { get; set; }

        public string? Outcome { get; set; }

        public string? Message { get; set; }

        public int? SnapshotIndex { get; set; }

        public ReplayMarkerMessage() { }

        public static ReplayMarkerMessage For(MarkerEvent marker) => new ReplayMarkerMessage
        {
            Marker = marker.Marker,
            Round = marker.Round,
            Activation = marker.Activation,
            PlayerId = marker.PlayerId,
            UnitRef = marker.UnitRef,
            Outcome = marker.Outcome,
            Message = marker.Message,
        };

        public static ReplayMarkerMessage ForSnapshot(int index) => new ReplayMarkerMessage { SnapshotIndex = index };

        /// <summary>The marker this message carries (for the client recorder), or null for a snapshot cue.</summary>
        public MarkerEvent? ToMarkerEvent() => Marker == null
            ? null
            : new MarkerEvent(Marker.Value)
            {
                Round = Round,
                Activation = Activation,
                PlayerId = PlayerId,
                UnitRef = UnitRef,
                Outcome = Outcome,
                Message = Message,
            };
    }
}
