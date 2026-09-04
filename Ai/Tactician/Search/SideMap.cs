using FDG.Data;
using FDG.Players;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The sides of one game, as a dense index (#191 B2, docs/tactician-b2-design.md sec 7.1):
    /// <see cref="SideValues"/> is indexed by SIDE, and a side is a team, not a player - teammates
    /// share a component by construction (G13c). Built once per tree from the root snapshot's slot
    /// records; PlayerIDs are stable across every snapshot of the same game (the simulation rebuilds
    /// slots on the saved IDs), so one map serves the whole tree.
    /// </summary>
    public sealed class SideMap
    {
        private readonly int[] _teamNumbers;
        private readonly Dictionary<PlayerID, int> _sideOfPlayer;

        private SideMap(int[] teamNumbers, Dictionary<PlayerID, int> sideOfPlayer)
        {
            _teamNumbers = teamNumbers;
            _sideOfPlayer = sideOfPlayer;
        }

        /// <summary>Number of sides (distinct team numbers) in the game.</summary>
        public int Count => _teamNumbers.Length;

        /// <summary>The side index of a player. Throws for a player the game does not know.</summary>
        public int SideOf(PlayerID player) =>
            _sideOfPlayer.TryGetValue(player, out int side)
                ? side
                : throw new KeyNotFoundException($"SideMap: player {player} is not in this game.");

        public int IndexOfTeam(int teamNumber)
        {
            int index = Array.IndexOf(_teamNumbers, teamNumber);
            return index >= 0
                ? index
                : throw new KeyNotFoundException($"SideMap: team {teamNumber} is not in this game.");
        }

        public int TeamNumberAt(int side) => _teamNumbers[side];

        public IEnumerable<PlayerID> Players => _sideOfPlayer.Keys;

        /// <summary>From a live or loaded store: one entry per filled player slot.</summary>
        public static SideMap FromStore(GameDataStore store) =>
            FromSlots(store.GetAllValues<PlayerSlotInfo>()
                .OrderBy(info => info.SlotID)
                .Select(info => (info.PlayerID, info.TeamNumber)));

        /// <summary>Authored (tests): explicit (player, team) pairs.</summary>
        public static SideMap FromSlots(IEnumerable<(PlayerID Player, int Team)> slots)
        {
            var list = slots.ToList();
            int[] teams = list.Select(s => s.Team).Distinct().OrderBy(t => t).ToArray();
            var map = new Dictionary<PlayerID, int>();
            foreach ((PlayerID player, int team) in list)
            {
                map[player] = Array.IndexOf(teams, team);
            }
            return new SideMap(teams, map);
        }
    }
}
