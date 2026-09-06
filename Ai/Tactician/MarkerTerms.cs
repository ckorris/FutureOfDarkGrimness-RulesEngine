using FDG.Ai.Tactician.Learning;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// The two marker terms the B-gate's failure analysis found missing from the leaf evaluator
    /// (#191 step 10 P4 "second half", 2026-09-06, from the 36 Orks transcripts: the Strategist
    /// left the centre marker contested while outnumbered there - contests resolved 20:7 against it -
    /// and never marched a spare unit onto a far marker, because between markers the old approach
    /// term was the CLOSEST unit to ANY marker over the table diagonal: saturated as soon as one unit
    /// sat on the home marker, and worth ~0.007 for a whole move's progress otherwise).
    /// <para>
    /// <b>Contest strength:</b> for every marker this side is in range of but does not own, its
    /// share of the unit VALUE both sides have inside the contest zone (seizure radius plus
    /// <see cref="ContestZoneExtraInches"/> - the bodies that will fight over it), summed over
    /// markers and divided by the marker count. A contest we out-mass is worth most of a
    /// contested marker; one where a horde stands on our toe-hold is worth little.
    /// <b>Open approach:</b> for every marker this side does not own, one minus the nearest eligible
    /// friendly unit's base-edge distance beyond the seizure radius over
    /// <see cref="ApproachScaleInches"/> (two rush moves), averaged over those markers - so walking
    /// a spare unit toward the enemy's flank marker is a slope from two moves out, per marker, and
    /// holding every marker reads as nothing left to approach.
    /// </para>
    /// Both are per-side and computed from the same projections the encoder uses; they are the v3
    /// encoder-feature candidates the step-11 C replan decides on (obj_contest_strength_share,
    /// obj_open_approach). Cost O(units x markers) base-edge distances, the encoder's own order.
    /// </summary>
    public static class MarkerTerms
    {
        public const float ContestZoneExtraInches = 3f;
        public const float ApproachScaleInches = 24f;

        public readonly record struct Result(float ContestStrength, float OpenApproach);

        public static Result Compute(ITableState state, IReadOnlyList<PlayerID> members,
            IReadOnlyList<PlayerID> opposing, List<ObjectiveProjection> projections, int objectiveCount)
        {
            if (projections.Count == 0) return new Result(0f, 1f);
            var memberList = members.ToList();
            var opposingList = opposing.ToList();
            List<IUnit> ours = Eligible(state, memberList);
            List<IUnit> theirs = Eligible(state, opposingList);
            float contestZone = TacticalAnalysis.ObjectiveSeizureRadiusInches + ContestZoneExtraInches;

            float strengthSum = 0f;
            float approachSum = 0f;
            int open = 0;
            foreach (ObjectiveProjection projection in projections)
            {
                bool ownedByUs = projection.ProjectedOwner.HasValue
                    && memberList.Contains(projection.ProjectedOwner.Value);
                if (ownedByUs) continue;
                Position marker = projection.Objective.Position;
                open++;

                float nearest = float.MaxValue;
                float ourMass = 0f;
                foreach (IUnit unit in ours)
                {
                    float d = TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, marker);
                    if (d < nearest) nearest = d;
                    if (d <= contestZone) ourMass += TacticalAnalysis.UnitValue(unit);
                }
                if (nearest < float.MaxValue)
                {
                    float beyond = Math.Max(0f, nearest - TacticalAnalysis.ObjectiveSeizureRadiusInches);
                    approachSum += Math.Clamp(1f - beyond / ApproachScaleInches, 0f, 1f);
                }

                bool weAreInRange = projection.PlayersInRange.Any(memberList.Contains);
                if (!weAreInRange) continue;
                float theirMass = 0f;
                foreach (IUnit unit in theirs)
                {
                    if (TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, marker) <= contestZone)
                        theirMass += TacticalAnalysis.UnitValue(unit);
                }
                float total = ourMass + theirMass;
                strengthSum += total <= 0f ? 0.5f : ourMass / total;
            }

            float strength = strengthSum / Math.Max(1, objectiveCount);
            float approach = open == 0 ? 1f : approachSum / open;
            return new Result(strength, approach);
        }

        // The units the reconcile rules count: living, on the table, seize-eligible.
        private static List<IUnit> Eligible(ITableState state, List<PlayerID> members)
        {
            var result = new List<IUnit>();
            foreach (IUnit unit in PositionEncoder.LivingUnits(state, members))
            {
                if (!TacticalAnalysis.CanSeizeObjectives(unit) || ReserveRules.IsInReserve(unit)) continue;
                result.Add(unit);
            }
            return result;
        }
    }
}
