using FDG.Data;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// Reads and writes the single <see cref="GameProgressData"/> instance that mirrors the
    /// game-flow position (round, turn cursor, unactivated units) into the store, and captures that
    /// position from the live <see cref="IMainPhaseContext"/> / <see cref="ISingleRoundContext"/>.
    /// <para>
    /// The capture call belongs at an activation boundary (the quiescent point between unit
    /// activations), where no transient combat/movement context exists. That wiring lands with the
    /// rolling-snapshot mechanism; this class is the data plumbing it builds on.
    /// </para>
    /// </summary>
    public static class GameProgressUtilities
    {
        /// <summary>The single progress record, or null if none has been written (or the type isn't registered).</summary>
        public static GameProgressData? TryGetProgress(IReadableGameDataStore store)
        {
            if (!store.IsTypeAssigned<GameProgressData>())
                return null;

            foreach (GameProgressData progress in store.GetAllValues<GameProgressData>())
                return progress;

            return null;
        }

        /// <summary>
        /// Writes <paramref name="progress"/> as the store's single progress record — updating the
        /// existing one in place (firing update events) if present, otherwise creating it.
        /// </summary>
        public static void WriteProgress(IReadWriteableGameDataStore store, GameProgressData progress)
        {
            foreach (DataReference existing in store.GetAllDataReferences<GameProgressData>())
            {
                store.SetValue(existing, progress);
                return;
            }

            store.Create(progress);
        }

        /// <summary>
        /// Snapshots the live round/turn position into a serializable <see cref="GameProgressData"/>.
        /// Teams are recorded by <see cref="ITeam.TeamNumber"/>; unactivated units are flattened
        /// across players (each unit carries its own <see cref="UnitData.PlayerID"/>).
        /// </summary>
        public static GameProgressData CaptureFromContexts(
            IMainPhaseContext mainPhase,
            ISingleRoundContext round,
            GameSettings settings,
            EResumeStage stage)
        {
            List<DataBinding<UnitData>> unactivated = new List<DataBinding<UnitData>>();
            foreach (KeyValuePair<PlayerID, List<DataBinding<UnitData>>> kvp in round.UnactivatedUnits)
                unactivated.AddRange(kvp.Value);

            List<int> teamOrder = new List<int>();
            foreach (ITeam team in round.TeamActivateOrder)
                teamOrder.Add(team.TeamNumber);

            List<int> finishOrder = new List<int>();
            foreach (ITeam team in round.CurrentRoundTeamFinishOrder)
                finishOrder.Add(team.TeamNumber);

            Dictionary<int, int> playerIndexByTeam = new Dictionary<int, int>();
            foreach (KeyValuePair<ITeam, int> kvp in round.CurrentActivePlayerIndexPerTeam)
                playerIndexByTeam[kvp.Key.TeamNumber] = kvp.Value;

            return new GameProgressData(
                stage: stage,
                roundCount: mainPhase.RoundCount,
                teamActivateOrder: teamOrder,
                currentRoundTeamFinishOrder: finishOrder,
                currentTeamIndex: round.CurrentActivatingTeamIndex,
                currentPlayerIndexPerTeam: playerIndexByTeam,
                unactivatedUnits: unactivated,
                settings: settings);
        }
    }
}
