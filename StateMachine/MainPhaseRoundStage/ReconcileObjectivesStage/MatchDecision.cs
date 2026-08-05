using FDG.Players;

namespace FDG.Stages
{
    /// <summary>
    /// #332: is the match's result already fixed, so that playing out the remaining rounds cannot change
    /// who wins? Reported from play - a player who held every objective and had tabled their opponent at
    /// the end of round 3 still had to hold every move through round 4 to reach a foregone conclusion.
    ///
    /// <para>
    /// The test is exact rather than a heuristic, and needs no reachability analysis at all. Two
    /// properties of the current rules carry it:
    /// <list type="number">
    /// <item>Objective ownership is sticky: <see cref="ITeamExtensions.ReconcileObjectiveOwner"/> keeps
    /// the current owner when nobody is in range, and <see cref="ReconcileObjectivesStage"/> does not even
    /// call <c>SetOwner</c> in that case. A marker is lost only to an enemy who physically gets within
    /// seizure range of it.</item>
    /// <item>So a side with no living models has a NON-INCREASING score - it can never seize or contest
    /// again - while the only side left standing has a NON-DECREASING one.</item>
    /// </list>
    /// A sole survivor who already leads therefore still leads at the end, whatever happens in between;
    /// and with every side wiped out the ownership map can never change again, so the standing result
    /// (win or tie) is already final.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT extended to "they lead 3-1 and the enemy cannot physically reach two markers in
    /// the rounds left": that needs movement budgets, terrain, Ambush arriving anywhere along a board
    /// edge, transports and Aircraft - which is exactly where a false positive (calling a match that was
    /// still live) would come from. The rule above cannot produce one.
    /// </para>
    /// </summary>
    public static class MatchDecision
    {
        /// <summary>
        /// True when no sequence of remaining legal play can change the outcome, so the game should skip
        /// straight to <see cref="VictoryCalculationStage"/>.
        /// <para>
        /// <paramref name="headline"/> is a short banner phrase and <paramref name="detail"/> a fuller log
        /// clause; both are ASCII and both are empty when the result is not yet fixed.
        /// </para>
        /// </summary>
        public static bool IsResultFixed(ITableState tableState, out string headline, out string detail)
        {
            headline = string.Empty;
            detail = string.Empty;

            List<ITeam> teams = tableState.Teams.Objects.ToList();
            List<List<PlayerID>> sides = GroupIntoSides(tableState, teams);

            // A single-sided table (a solo test scenario, or a game whose armies all belong to one player)
            // is trivially "decided", but ending it at the first round end would be a surprising change to
            // those setups and buys nothing - there is no opponent being made to wait out a foregone
            // conclusion. Only a real match gets called early.
            if (sides.Count < 2) return false;

            int[] living = new int[sides.Count];
            int[] scores = new int[sides.Count];

            // Living means alive ANYWHERE - never GetIsOnBattlefield. An Ambush unit still in reserve, a
            // squad embarked in a transport, an Aircraft that flew off the edge, and the full-strength copy
            // Reinforcement queues when a unit is destroyed are all off-table and all still in the game.
            // Reading on-table presence here is the one mistake that would end a match that is still live.
            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (!unit.GetIsAlive()) continue;
                int side = SideOf(sides, unit.PlayerID);
                if (side >= 0) living[side]++;
            }

            foreach (IObjective objective in tableState.Objectives.Objects)
            {
                if (!objective.OwnerID.HasValue) continue;
                int side = SideOf(sides, objective.OwnerID.Value);
                if (side >= 0) scores[side]++;
            }

            List<int> standing = new List<int>();
            for (int i = 0; i < sides.Count; i++)
                if (living[i] > 0) standing.Add(i);

            // Two or more sides can still move, so anything can still happen.
            if (standing.Count > 1) return false;

            Dictionary<PlayerID, string> names = NamesByPlayer(tableState);

            if (standing.Count == 0)
            {
                // Nobody can move, so no objective can change hands again: the board is frozen and whatever
                // it says right now - win or tie - is the final result.
                headline = "No forces remain on either side";
                detail = "no living units left on any side, so no objective can change hands again";
                return true;
            }

            int survivor = standing[0];

            // The survivor's score can only rise (nobody is left to contest what they hold) and every other
            // side's can only fall, so a SOLE lead now is a sole lead at the end. A tie is not enough: the
            // survivor could still break it by walking onto a marker, and that is a game worth playing out.
            for (int i = 0; i < sides.Count; i++)
                if (i != survivor && scores[i] >= scores[survivor]) return false;

            List<int> eliminated = new List<int>();
            for (int i = 0; i < sides.Count; i++)
                if (i != survivor) eliminated.Add(i);

            headline = "No opposing forces remain";
            detail = $"no living units left for {DescribeSides(sides, eliminated, names)}, " +
                "so the lead can no longer be caught";
            return true;
        }

        /// <summary>
        /// Sides in first-seen order: every player that owns a unit, holds an objective, or fills a slot,
        /// grouped by <see cref="ITeamExtensions.AreAllied"/> - the authority for "same side", under which
        /// a player on no team is allied only with itself, so 1v1 and solo play group exactly as before.
        /// The player set is deliberately wider than the filled slots, because
        /// <see cref="VictoryCalculationStage"/> also tallies an objective owner that has no slot.
        /// </summary>
        private static List<List<PlayerID>> GroupIntoSides(ITableState tableState, List<ITeam> teams)
        {
            List<PlayerID> seen = new List<PlayerID>();
            void Note(PlayerID id)
            {
                if (!seen.Contains(id)) seen.Add(id);
            }

            foreach (IPlayerSlotInfo slot in tableState.Players.Objects)
                if (slot.IsFilled) Note(slot.PlayerID);
            foreach (IUnit unit in tableState.Units.Objects)
                Note(unit.PlayerID);
            foreach (IObjective objective in tableState.Objectives.Objects)
                if (objective.OwnerID.HasValue) Note(objective.OwnerID.Value);

            List<List<PlayerID>> sides = new List<List<PlayerID>>();
            foreach (PlayerID id in seen)
            {
                List<PlayerID>? side = sides.FirstOrDefault(
                    existing => ITeamExtensions.AreAllied(teams, existing[0], id));
                if (side == null) sides.Add(new List<PlayerID> { id });
                else side.Add(id);
            }

            return sides;
        }

        private static int SideOf(List<List<PlayerID>> sides, PlayerID player)
        {
            for (int i = 0; i < sides.Count; i++)
                if (sides[i].Contains(player)) return i;
            return -1;
        }

        private static Dictionary<PlayerID, string> NamesByPlayer(ITableState tableState)
        {
            Dictionary<PlayerID, string> names = new Dictionary<PlayerID, string>();
            foreach (IPlayerSlotInfo slot in tableState.Players.Objects)
                if (slot.IsFilled && !string.IsNullOrWhiteSpace(slot.Name))
                    names[slot.PlayerID] = slot.Name;
            return names;
        }

        // "Blue" / "Blue and Green" / "Blue, Green and Red". A side whose players hold no named slot is
        // described generically rather than by a raw GUID. ASCII only - this reaches the log.
        private static string DescribeSides(List<List<PlayerID>> sides, List<int> indices,
            Dictionary<PlayerID, string> names)
        {
            List<string> labels = new List<string>();
            foreach (int index in indices)
            {
                List<string> members = sides[index]
                    .Where(names.ContainsKey).Select(player => names[player]).ToList();
                labels.Add(members.Count > 0 ? string.Join(" and ", members) : "the other side");
            }

            if (labels.Count == 0) return "the other side";
            if (labels.Count == 1) return labels[0];
            return string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels[labels.Count - 1];
        }
    }
}
