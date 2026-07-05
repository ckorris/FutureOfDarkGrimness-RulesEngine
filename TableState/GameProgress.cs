using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;

namespace FDG
{
    /// <summary>
    /// A player's controlled-objective tally, for the live scoreboard.
    /// </summary>
    public readonly struct PlayerObjectiveScore
    {
        public PlayerID PlayerID { get; }
        public int ObjectiveCount { get; }

        public PlayerObjectiveScore(PlayerID playerID, int objectiveCount)
        {
            PlayerID = playerID;
            ObjectiveCount = objectiveCount;
        }
    }

    /// <summary>
    /// The player-facing read model of "where the game is": round number, objective scores, the unit
    /// currently activating, and whatever else the front end needs at a glance. Exposed once via
    /// <see cref="ITableState.Progress"/> and read as live state (queried each frame like units or
    /// objectives) rather than scraped from log/banner text.
    /// <para>
    /// Deliberately a single aggregate so new progression facts can be added here without churning the
    /// <see cref="ITableState"/> surface. Backed by the replicated <see cref="GameProgressData"/> (round,
    /// activating unit) plus the live objectives/players (scores), so it works on host and client alike.
    /// </para>
    /// </summary>
    public interface IGameProgress
    {
        /// <summary>1-based round number, or null before the main phase begins (no progress record yet).</summary>
        int? RoundCount { get; }

        /// <summary>Total rounds in a game (<see cref="GameWideConstants.NUMBER_OF_ROUNDS"/>).</summary>
        int TotalRounds { get; }

        /// <summary>The unit taking its activation right now, or null between activations / outside the main phase.</summary>
        IUnit? ActivatingUnit { get; }

        /// <summary>Controlled-objective counts, one entry per filled player slot, in slot order.</summary>
        IReadOnlyList<PlayerObjectiveScore> Scores { get; }
    }

    /// <summary>
    /// <see cref="IGameProgress"/> over an <see cref="IReadableGameDataStore"/>. Every property reads
    /// through to the store on access, so the values are always current with no caching or events to keep
    /// in sync -- the renderer polls it each frame.
    /// </summary>
    public sealed class GameProgress : IGameProgress
    {
        private readonly IReadableGameDataStore _store;

        public GameProgress(IReadableGameDataStore store) => _store = store;

        public int TotalRounds => GameWideConstants.NUMBER_OF_ROUNDS;

        public int? RoundCount => GameProgressUtilities.TryGetProgress(_store)?.RoundCount;

        public IUnit? ActivatingUnit => GameProgressUtilities.TryGetProgress(_store)?.ActivatingUnit?.GetValue();

        public IReadOnlyList<PlayerObjectiveScore> Scores
        {
            get
            {
                List<ObjectiveData> objectives = _store.GetAllValues<ObjectiveData>().ToList();
                var scores = new List<PlayerObjectiveScore>();
                foreach (PlayerSlotInfo slot in _store.GetAllValues<PlayerSlotInfo>())
                {
                    if (!slot.IsFilled) continue;
                    int count = objectives.Count(o => o.OwnerID.HasValue && o.OwnerID.Value.Equals(slot.PlayerID));
                    scores.Add(new PlayerObjectiveScore(slot.PlayerID, count));
                }
                return scores;
            }
        }
    }
}
