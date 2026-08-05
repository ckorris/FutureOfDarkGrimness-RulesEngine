using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Advance lanes of friendlies that have not activated yet this round (#359, the lane-clearing
    /// half of Chris's crowded-game remedy - #296 built the ordering half). Each such friendly owns
    /// a lane from where it stands toward the enemy mass, as far as its Rush reaches; a unit that
    /// ENDS its own move on someone else's lane is the wall the rear ranks halve their moves
    /// against. The planner charges endpoints for blocking (MoveLaneBlock) and the generator offers
    /// side-steps (M13) when the active unit is itself standing on one.
    /// <para>
    /// Centroid geometry, same style as the A5-4 screen lane. Known v1 coarseness, recorded in the
    /// #359 ledger: a wide formation can straddle a lane its centroid clears, and a friendly's real
    /// goal may not lie toward the enemy mass at all (a marker off to a flank). Both are acceptable
    /// for the round-1 packed shape this exists for, where "forward" and "toward the enemy" agree.
    /// </para>
    /// </summary>
    public static class LaneGeometry
    {
        /// <summary>Blocking counts fully within this lateral distance of the lane...</summary>
        public const float FullBlockInches = 1.5f;
        /// <summary>...and fades to nothing here (about a base-to-base corridor width).</summary>
        public const float ClearInches = 4f;

        /// <summary>One unactivated friendly's advance lane; Weight is its UnitValue / 100.</summary>
        public readonly record struct AdvanceLane(Position From, Position To, float Weight);

        /// <summary>
        /// The advance lanes every OTHER living, fielded, not-yet-activated allied unit owns right
        /// now. Empty when no enemy is fielded (no axis to advance along). Endpoint-independent -
        /// build once per activation and query per candidate.
        /// </summary>
        public static List<AdvanceLane> Build(ITableState tableState, RuleEvaluator evaluator,
            UnitData self)
        {
            var lanes = new List<AdvanceLane>();

            float enemyX = 0f, enemyZ = 0f;
            int enemies = 0;
            foreach (UnitData enemy in AliveFieldedUnits(tableState, self, allied: false))
            {
                Position at = Centroid(enemy);
                enemyX += at.x; enemyZ += at.z; enemies++;
            }
            if (enemies == 0) return lanes;
            var enemyMass = new Position(enemyX / enemies, enemyZ / enemies);

            foreach (UnitData friend in AliveFieldedUnits(tableState, self, allied: true))
            {
                if (ReferenceEquals(friend, self)) continue;
                if (friend.Tokens.HasToken(TokenType.ActivatedThisRound)) continue;

                Position at = Centroid(friend);
                float dx = enemyMass.x - at.x, dz = enemyMass.z - at.z;
                float length = MathF.Sqrt(dx * dx + dz * dz);
                if (length < 0.001f) continue;
                float rush = TacticalAnalysis.RushDistance(friend, evaluator);
                if (rush <= 0f) continue;

                lanes.Add(new AdvanceLane(at,
                    new Position(at.x + dx / length * rush, at.z + dz / length * rush),
                    TacticalAnalysis.UnitValue(friend) / 100f));
            }
            return lanes;
        }

        /// <summary>
        /// Value-weighted blocking at <paramref name="at"/>: for each lane, the owner's weight
        /// scaled by how squarely the point sits on it (full within <see cref="FullBlockInches"/>,
        /// zero past <see cref="ClearInches"/>) AND by how much of the owner's move the block cuts
        /// off - full right in front of the friendly, fading to nothing at the tip of its reach
        /// (Chris's correction at review: walking FORWARD along the lane also clears it - the
        /// friendly steps into the vacated ground - so an advance that ends deep downrange must
        /// not price like standing still; only ending in the NEAR corridor walls the move).
        /// Summed - walling two friendlies is worse than one. Behind the friendly costs nothing.
        /// </summary>
        public static float BlockValue(IReadOnlyList<AdvanceLane> lanes, Position at)
        {
            float total = 0f;
            foreach (AdvanceLane lane in lanes)
            {
                float abx = lane.To.x - lane.From.x, abz = lane.To.z - lane.From.z;
                float lengthSq = abx * abx + abz * abz;
                if (lengthSq <= 0.0001f) continue;
                float t = ((at.x - lane.From.x) * abx + (at.z - lane.From.z) * abz) / lengthSq;
                if (t <= 0f || t >= 1f) continue; // behind the friendly, or past its whole reach
                float lateral = Distance(at,
                    new Position(lane.From.x + t * abx, lane.From.z + t * abz));
                float squareness =
                    Math.Clamp((ClearInches - lateral) / (ClearInches - FullBlockInches), 0f, 1f);
                total += lane.Weight * squareness * (1f - t);
            }
            return total;
        }

        // Same side semantics as the planner's Friendly/EnemyBindings (#296 team-aware: a 2v2
        // teammate's rear ranks deserve clear lanes too, and its units are enemy mass to no one
        // on their own side).
        private static IEnumerable<UnitData> AliveFieldedUnits(ITableState tableState,
            UnitData self, bool allied)
        {
            foreach (IArmy army in tableState.Armies.Objects)
            {
                if (TacticalAnalysis.AreAllied(tableState, self.PlayerID, army.PlayerID) != allied
                    || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                {
                    UnitData value = unit.GetValue();
                    if (value.Models.Any(m => m.GetIsAlive()) && value.GetIsOnBattlefield())
                        yield return value;
                }
            }
        }

        private static Position Centroid(UnitData unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return new Position(0f, 0f);
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
