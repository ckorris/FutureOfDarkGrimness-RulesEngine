using FDG.Ai.Tactician.Learning;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Last-round counting (#191 step 10 P1, from Chris's "micro-managing objective holding toward
    /// the end" and the 2026-09-05 thinking pass): who holds what when the round ENDS, given the
    /// units both sides still have to activate. <see cref="TacticalAnalysis.ProjectObjectives"/>
    /// answers "if the round ended now"; in the final round that is the wrong question, because
    /// every unactivated unit still gets one move and the reconcile rules make one body within 3"
    /// a full denial. The evaluator's per-marker yes/no could not count ("they have three units that
    /// can reach my two markers and I have one responder" was invisible), so it is projected here.
    /// <para>
    /// Greedy, deterministic, O(units x markers). Movers are the unactivated, seize-eligible units
    /// not already standing on a marker (a unit on one stays - leaving is never assumed). Pass 1:
    /// every marker a side currently holds is denied (neutral) if an opposing mover can reach it
    /// (rush + seizure radius); a sticky-held marker with no holder in range is instead SEIZED by
    /// the one opposing side that can reach it, or left neutral if the holder's side can answer.
    /// Pass 2: a neutral marker goes to the one side whose mover can reach it, and stays neutral in
    /// a standoff (both can). A mover is spent by its assignment, which is the whole point - one
    /// unit cannot deny two markers. Pass 3 (uncertain): a marker still held after the passes but
    /// inside an unspent enemy's shooting reach is flagged threatened (the holder can be shot off;
    /// the evaluator counts it half). Nothing here is a value; the evaluator blends this tally with
    /// the current projection at <see cref="HandWeightedEvaluatorConfidence"/>.
    /// </para>
    /// </summary>
    public static class RoundEndProjection
    {
        /// <summary>
        /// How much of a projected (not yet realized) marker state the evaluator believes: a mover
        /// "can reach" is not "will end there and survive", and the in-sim policy may send it
        /// elsewhere. Realized states (a body in range now) are certain, so walking onto a marker
        /// you were already projected to take is still worth the remaining quarter - the leaf keeps
        /// its gradient in the last round.
        /// </summary>
        public const float HandWeightedEvaluatorConfidence = 0.75f;

        /// <summary>One side's round-end tally: markers projected held, and how many of those an unspent enemy shooter still reaches.</summary>
        public readonly record struct SideTally(int Held, int Threatened);

        public static bool IsLastRound(ITableState state)
        {
            int totalRounds = Math.Max(1, state.Progress.TotalRounds);
            return (state.Progress.RoundCount ?? 1) >= totalRounds;
        }

        /// <summary>
        /// The round-end tally per side. <paramref name="sides"/> lists each side's member players;
        /// a marker's current owner outside every side (never, in practice) reads as neutral.
        /// </summary>
        public static SideTally[] Project(ITableState state, RuleEvaluator evaluator,
            IReadOnlyList<IReadOnlyList<PlayerID>> sides)
        {
            int sideCount = sides.Count;
            var terrain = TacticalAnalysis.TerrainOf(state);
            List<ObjectiveProjection> projections = TacticalAnalysis.ProjectObjectives(state);
            int markerCount = projections.Count;
            var tallies = new SideTally[sideCount];
            if (markerCount == 0 || sideCount == 0) return tallies;

            int SideOf(PlayerID player)
            {
                for (int s = 0; s < sideCount; s++)
                    if (sides[s].Contains(player)) return s;
                return -1;
            }

            // Current state per marker: projected owner side, and whether that side has a body in range
            // (a sticky holding from an earlier round has none - it can be taken outright).
            var baseSide = new int[markerCount];
            var holderInRange = new bool[markerCount];
            for (int m = 0; m < markerCount; m++)
            {
                ObjectiveProjection p = projections[m];
                baseSide[m] = p.ProjectedOwner.HasValue ? SideOf(p.ProjectedOwner.Value) : -1;
                holderInRange[m] = baseSide[m] >= 0
                    && p.PlayersInRange.Any(player => SideOf(player) == baseSide[m]);
            }

            // Movers: unactivated, seize-eligible, off every marker. Reach per marker, both kinds.
            var movers = new List<Mover>();
            for (int s = 0; s < sideCount; s++)
            {
                foreach (IUnit unit in PositionEncoder.LivingUnits(state, sides[s].ToList()))
                {
                    if (unit.Tokens.HasToken(TokenType.ActivatedThisRound)) continue;
                    if (!TacticalAnalysis.CanSeizeObjectives(unit) || ReserveRules.IsInReserve(unit)) continue;
                    var distances = new float[markerCount];
                    bool onAMarker = false;
                    for (int m = 0; m < markerCount; m++)
                    {
                        distances[m] = TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, projections[m].Objective.Position);
                        onAMarker |= distances[m] <= TacticalAnalysis.ObjectiveSeizureRadiusInches;
                    }
                    if (onAMarker) continue;

                    float moveReach = TacticalAnalysis.RushDistance(unit, evaluator, terrain)
                        + TacticalAnalysis.ObjectiveSeizureRadiusInches;
                    float shootReach = PositionEncoder.CheapThreatReach(unit, evaluator, terrain)
                        + TacticalAnalysis.ObjectiveSeizureRadiusInches;
                    var canMove = new bool[markerCount];
                    var canShoot = new bool[markerCount];
                    int reachable = 0;
                    for (int m = 0; m < markerCount; m++)
                    {
                        canMove[m] = distances[m] <= moveReach;
                        canShoot[m] = distances[m] <= shootReach;
                        if (canMove[m]) reachable++;
                    }
                    if (reachable == 0 && !canShoot.Any(c => c)) continue;
                    movers.Add(new Mover(s, canMove, canShoot, reachable));
                }
            }

            var final = (int[])baseSide.Clone();
            var threatened = new bool[markerCount];

            // The least flexible unspent mover of a side that can reach the marker - spending the
            // unit with the fewest alternatives leaves the flexible ones for the other markers.
            Mover? Pick(int side, int marker, bool byMove)
            {
                Mover? best = null;
                foreach (Mover mover in movers)
                {
                    if (mover.Spent || mover.Side != side) continue;
                    if (!(byMove ? mover.CanMove[marker] : mover.CanShoot[marker])) continue;
                    if (best == null || mover.Reachable < best.Reachable) best = mover;
                }
                return best;
            }

            bool AnyUnspent(int side, int marker) =>
                movers.Any(mv => !mv.Spent && mv.Side == side && mv.CanMove[marker]);

            // Pass 1: currently held markers - denial (holder in range) or seizure (sticky, nobody there).
            for (int m = 0; m < markerCount; m++)
            {
                if (baseSide[m] < 0) continue;
                if (holderInRange[m])
                {
                    for (int s = 0; s < sideCount; s++)
                    {
                        if (s == baseSide[m]) continue;
                        Mover? denier = Pick(s, m, byMove: true);
                        if (denier == null) continue;
                        denier.Spent = true;
                        final[m] = -1;
                        break;
                    }
                }
                else
                {
                    int reachingSide = -1;
                    bool standoff = false;
                    for (int s = 0; s < sideCount; s++)
                    {
                        if (!AnyUnspent(s, m)) continue;
                        if (reachingSide >= 0) { standoff = true; break; }
                        reachingSide = s;
                    }
                    if (standoff) final[m] = -1;               // nobody bothers, it ends neutral
                    else if (reachingSide >= 0 && reachingSide != baseSide[m])
                    {
                        Pick(reachingSide, m, byMove: true)!.Spent = true;
                        final[m] = reachingSide;
                    }
                    // else: the owner can answer or nobody reaches - it stays theirs by stickiness.
                }
            }

            // Pass 2: neutral markers - the one side that can reach it takes it; a standoff stays neutral.
            for (int m = 0; m < markerCount; m++)
            {
                if (baseSide[m] >= 0) continue;
                int reachingSide = -1;
                bool standoff = false;
                for (int s = 0; s < sideCount; s++)
                {
                    if (!AnyUnspent(s, m)) continue;
                    if (reachingSide >= 0) { standoff = true; break; }
                    reachingSide = s;
                }
                if (standoff || reachingSide < 0) continue;
                Pick(reachingSide, m, byMove: true)!.Spent = true;
                final[m] = reachingSide;
            }

            // Pass 3: what is still held but inside an unspent enemy's guns.
            for (int m = 0; m < markerCount; m++)
            {
                if (final[m] < 0) continue;
                for (int s = 0; s < sideCount; s++)
                {
                    if (s == final[m]) continue;
                    Mover? shooter = Pick(s, m, byMove: false);
                    if (shooter == null) continue;
                    shooter.Spent = true;
                    threatened[m] = true;
                    break;
                }
            }

            for (int s = 0; s < sideCount; s++)
            {
                int held = 0, threat = 0;
                for (int m = 0; m < markerCount; m++)
                {
                    if (final[m] != s) continue;
                    held++;
                    if (threatened[m]) threat++;
                }
                tallies[s] = new SideTally(held, threat);
            }
            return tallies;
        }

        private sealed class Mover
        {
            public readonly int Side;
            public readonly bool[] CanMove;
            public readonly bool[] CanShoot;
            public readonly int Reachable;
            public bool Spent;

            public Mover(int side, bool[] canMove, bool[] canShoot, int reachable)
            {
                Side = side;
                CanMove = canMove;
                CanShoot = canShoot;
                Reachable = reachable;
            }
        }
    }
}
