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
        /// across players (each unit carries its own <see cref="UnitData.PlayerID"/>). The round
        /// number is read from <see cref="ISingleRoundContext.RoundCount"/>, which carries the value
        /// down from the <see cref="IMainPhaseContext"/>.
        /// </summary>
        public static GameProgressData Capture(
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
                roundCount: round.RoundCount,
                teamActivateOrder: teamOrder,
                currentRoundTeamFinishOrder: finishOrder,
                currentTeamIndex: round.CurrentActivatingTeamIndex,
                currentPlayerIndexPerTeam: playerIndexByTeam,
                unactivatedUnits: unactivated,
                settings: settings);
        }

        /// <summary>Rebuilds the main-phase context (round number + activation order) from a snapshot.</summary>
        public static MainPhaseContext RestoreMainPhaseContext(IGameContext gameContext, GameProgressData progress)
        {
            List<ITeam> allTeams = gameContext.TableState.Teams.Objects.ToList();
            return new MainPhaseContext(gameContext, MapTeams(allTeams, progress.TeamActivateOrder), progress.RoundCount);
        }

        /// <summary>
        /// Rebuilds the in-progress round context from a snapshot: maps team numbers back to the
        /// store's canonical <see cref="ITeam"/> instances, restores the cursor + finish order, and
        /// regroups the unactivated units by owner (with an entry for every player so
        /// <see cref="ISingleRoundContext.MarkUnitAsActivated"/> never misses a key).
        /// </summary>
        public static SingleRoundContext RestoreSingleRoundContext(IGameContext gameContext, GameProgressData progress)
        {
            List<ITeam> allTeams = gameContext.TableState.Teams.Objects.ToList();
            List<ITeam> teamOrder = MapTeams(allTeams, progress.TeamActivateOrder);
            List<ITeam> finishOrder = MapTeams(allTeams, progress.CurrentRoundTeamFinishOrder);

            Dictionary<ITeam, int> playerIndexPerTeam = new Dictionary<ITeam, int>();
            foreach (KeyValuePair<int, int> kvp in progress.CurrentPlayerIndexPerTeam)
            {
                playerIndexPerTeam[FindTeam(allTeams, kvp.Key)] = kvp.Value;
            }

            Dictionary<PlayerID, List<DataBinding<UnitData>>> unactivated = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            foreach (ITeam team in allTeams)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    if (!unactivated.ContainsKey(playerID))
                    {
                        unactivated[playerID] = new List<DataBinding<UnitData>>();
                    }
                }
            }

            foreach (DataBinding<UnitData> unit in progress.UnactivatedUnits)
            {
                PlayerID owner = unit.GetValue().PlayerID;
                if (!unactivated.ContainsKey(owner))
                {
                    unactivated[owner] = new List<DataBinding<UnitData>>();
                }

                unactivated[owner].Add(unit);
            }

            return new SingleRoundContext(gameContext, teamOrder, progress.RoundCount,
                progress.CurrentTeamIndex, playerIndexPerTeam, finishOrder, unactivated);
        }

        private static List<ITeam> MapTeams(IReadOnlyList<ITeam> allTeams, List<int> teamNumbers)
        {
            List<ITeam> result = new List<ITeam>(teamNumbers.Count);
            foreach (int teamNumber in teamNumbers)
            {
                result.Add(FindTeam(allTeams, teamNumber));
            }

            return result;
        }

        private static ITeam FindTeam(IReadOnlyList<ITeam> allTeams, int teamNumber)
        {
            foreach (ITeam team in allTeams)
            {
                if (team.TeamNumber == teamNumber)
                {
                    return team;
                }
            }

            throw new InvalidOperationException(
                $"Saved game references team {teamNumber}, which isn't present in the restored store.");
        }
    }
}
