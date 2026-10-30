namespace FDG.Replay
{
    /// <summary>Which side of the game wrote a recording (plan 4.1). Host and client recordings of one game
    /// share <see cref="ReplayHeader.GameId"/> and request <c>TaskId</c>s, never <c>Seq</c>.</summary>
    public enum EReplayRecordedBy
    {
        Host,
        Client,
    }

    /// <summary>Who answered a slot's decisions, from the recording side's point of view.</summary>
    public enum EReplayController
    {
        /// <summary>A person at the recording machine (a <c>LocalPlayerController</c>).</summary>
        Human,

        /// <summary>Any computer controller (solo-rules, Tactician, scenario AI).</summary>
        Ai,

        /// <summary>A person on another machine (a <c>NetworkPlayerController</c>).</summary>
        Remote,
    }

    /// <summary>One player slot as it stood when the recording started.</summary>
    public sealed class ReplaySlot
    {
        public int SlotId { get; set; }

        public PlayerID PlayerId { get; set; }

        public string Name { get; set; } = "";

        public string ArmyName { get; set; } = "";

        public int Team { get; set; }

        public EReplayController Controller { get; set; }
    }

    /// <summary>Line 1 of a <c>.fdgreplay</c> (plan 4.1).</summary>
    public sealed class ReplayHeader
    {
        public int FormatVersion { get; set; } = ReplayFormat.CurrentVersion;

        /// <summary>The app build that recorded this (<see cref="ReplayFormat.BuildVersion"/>).</summary>
        public string BuildVersion { get; set; } = "";

        /// <summary>The engine build that recorded this (<see cref="ReplayFormat.EngineVersion"/>).</summary>
        public string EngineVersion { get; set; } = "";

        /// <summary>
        /// The game's persistent identity (<see cref="GameProgressData.GameId"/>): minted once when the game
        /// is created, carried through every save and resume, and shared by the host's and every client's
        /// recording of the same game. Distinct from the lobby's per-launch analytics ID (#412).
        /// </summary>
        public Guid GameId { get; set; }

        /// <summary>The dice seed the game ran under. Never null for a host recording since #450 (D5): an
        /// unseeded launch has one picked for it.</summary>
        public int? Seed { get; set; }

        public ERandomnessType RandomnessType { get; set; }

        public GameSettings Settings { get; set; }

        public List<ReplaySlot> Slots { get; set; } = new List<ReplaySlot>();

        public EReplayRecordedBy RecordedBy { get; set; }

        /// <summary>The (first) human player at the recording machine, or null when none is (a host with only
        /// AI and remote slots).</summary>
        public PlayerID? RecordedByPlayerId { get; set; }

        public DateTime StartedUtc { get; set; }
    }
}
