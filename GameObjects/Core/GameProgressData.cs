using FDG.Data;
using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// The "where are we in the game flow" state, promoted into the <see cref="GameDataStore"/> so
    /// that it serializes for save/load and replicates to clients for networked play, exactly like
    /// the rest of the game world. It mirrors the transient round/turn state that otherwise lives
    /// only in <see cref="FDG.Stages.MainPhaseContext"/> and
    /// <see cref="FDG.Stages.SingleRoundContext"/> (which are recreated each round and never
    /// persisted).
    /// <para>
    /// This is the raw persisted record; the player-facing read model over it (round, scores, the
    /// activating unit) is <see cref="IGameProgress"/>, exposed via <see cref="ITableState.Progress"/>.
    /// </para>
    /// <para>
    /// Teams are referenced by <see cref="ITeam.TeamNumber"/> — a stable, serializable identifier —
    /// rather than by <see cref="ITeam"/> object reference. Units are referenced by
    /// <see cref="DataBinding{T}"/>, which serializes to a bare <see cref="DataReference"/>.
    /// </para>
    /// <para>There is exactly one of these in a live store once the main phase has begun.</para>
    /// </summary>
    public class GameProgressData
    {
        /// <summary>Which top-level stage the game is in, so a load can re-enter the right place.</summary>
        public EResumeStage Stage;

        /// <summary>1-based round number (mirrors <see cref="FDG.Stages.IMainPhaseContext.RoundCount"/>).</summary>
        public int RoundCount { get; set; }

        /// <summary>
        /// The unit currently taking its activation, or null between activations (and outside the main
        /// phase). Set at the start of a unit's activation and cleared at its end, so the read model can
        /// spotlight it. Serializes to a bare <see cref="DataReference"/> like the other unit references.
        /// </summary>
        public DataBinding<UnitData>? ActivatingUnit { get; set; }

        /// <summary>
        /// This round's team activation order, by <see cref="ITeam.TeamNumber"/>. Equal to the
        /// activation cursor's team order.
        /// </summary>
        public List<int> TeamActivateOrder;

        /// <summary>
        /// Teams (by <see cref="ITeam.TeamNumber"/>) that have finished all activations this round,
        /// in the order they finished. Seeds the next round's activation order.
        /// </summary>
        public List<int> CurrentRoundTeamFinishOrder;

        /// <summary>The activation cursor's current index into <see cref="TeamActivateOrder"/>.</summary>
        public int CurrentTeamIndex;

        /// <summary>Per-team round-robin player index, keyed by <see cref="ITeam.TeamNumber"/>.</summary>
        public Dictionary<int, int> CurrentPlayerIndexPerTeam;

        /// <summary>
        /// Units (flattened across all players) that have NOT yet activated this round. On resume the
        /// per-player grouping is rebuilt from each unit's <see cref="UnitData.PlayerID"/>.
        /// </summary>
        public List<DataBinding<UnitData>> UnactivatedUnits;

        /// <summary>The game's configured settings, needed to faithfully resume the same game.</summary>
        public GameSettings Settings;

        [JsonConstructor]
        public GameProgressData(
            EResumeStage stage,
            int roundCount,
            List<int> teamActivateOrder,
            List<int> currentRoundTeamFinishOrder,
            int currentTeamIndex,
            Dictionary<int, int> currentPlayerIndexPerTeam,
            List<DataBinding<UnitData>> unactivatedUnits,
            GameSettings settings,
            DataBinding<UnitData>? activatingUnit = null)
        {
            Stage = stage;
            RoundCount = roundCount;
            TeamActivateOrder = teamActivateOrder;
            CurrentRoundTeamFinishOrder = currentRoundTeamFinishOrder;
            CurrentTeamIndex = currentTeamIndex;
            CurrentPlayerIndexPerTeam = currentPlayerIndexPerTeam;
            UnactivatedUnits = unactivatedUnits;
            Settings = settings;
            ActivatingUnit = activatingUnit;
        }
    }

    /// <summary>
    /// The top-level stage a saved game was in, so a load can re-enter the state machine at the
    /// right place rather than restarting from map setup.
    /// </summary>
    public enum EResumeStage
    {
        MapSetup,
        Deployment,
        MainPhase,
        VictoryCalculation,
    }
}
