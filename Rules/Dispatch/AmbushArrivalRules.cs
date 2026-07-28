using FDG.StageResolution.Requests;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Builds the relational placement constraints an Ambush reserve arrival is subject to (#197 P22):
    /// keep-out discs from enemy units with <c>Repel Ambushers</c> and waiver discs from friendly units
    /// with <c>Ambush Beacon</c>. Called once at request build (`StartOfRoundExtraActionStage`) — enemy
    /// positions cannot change while the arriving player is placing, so a snapshot is exact.
    ///
    /// <para>Both scans are SIDE-aware (<see cref="ITeamExtensions.AreAllied"/>): a teammate's repel
    /// rule does not push its own side's Ambushers away, and a teammate's beacon lights the way for the
    /// whole side — "friendly"/"enemy" in the corpus wording is relative to the arriving unit.</para>
    ///
    /// <para>Discs radiate from every LIVING model of the constraint-bearing unit. For Repel that is the
    /// wording exactly ("over 12\" away from this model's UNIT"); for Beacon ("within 6\" of this
    /// model") it is the same unit-holds-the-rule accommodation Spell Accumulator and Caster Group made —
    /// there is no per-model attribution at the evaluator seam, and every corpus beacon is a
    /// single-model unit, so nothing observable rides on it.</para>
    /// </summary>
    public static class AmbushArrivalRules
    {
        /// <summary>
        /// The keep-out discs enemy <c>Repel Ambushers</c> units impose on <paramref name="arriving"/>'s
        /// placement: one disc per living model of each repelling unit, at that unit's repel distance.
        /// </summary>
        public static IReadOnlyList<PlacementDisc> KeepOutDiscs(IUnit arriving, ITableState tableState,
            RuleEvaluator evaluator)
        {
            return BuildDiscs(arriving, tableState, evaluator, wantAllied: false,
                unit => CapabilityRuleQueries.AmbushRepelDistance(unit, evaluator));
        }

        /// <summary>
        /// The waiver discs friendly <c>Ambush Beacon</c> units offer <paramref name="arriving"/>'s
        /// placement: one disc per living model of each beacon unit, at that unit's beacon range.
        /// </summary>
        public static IReadOnlyList<PlacementDisc> WaiverDiscs(IUnit arriving, ITableState tableState,
            RuleEvaluator evaluator)
        {
            return BuildDiscs(arriving, tableState, evaluator, wantAllied: true,
                unit => CapabilityRuleQueries.AmbushBeaconRange(unit, evaluator));
        }

        private static IReadOnlyList<PlacementDisc> BuildDiscs(IUnit arriving, ITableState tableState,
            RuleEvaluator evaluator, bool wantAllied, Func<IUnit, float> radiusOf)
        {
            List<PlacementDisc>? discs = null;
            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (ReferenceEquals(unit, arriving)) continue;
                if (!unit.GetIsAlive()) continue;
                // A unit still in reserve has no table presence to measure from (and a reserve model's
                // stale/origin position must not project a phantom disc).
                if (ReserveRules.IsInReserve(unit)) continue;
                if (ITeamExtensions.AreAllied(tableState.Teams.Objects, arriving.PlayerID, unit.PlayerID)
                    != wantAllied)
                {
                    continue;
                }

                float radius = radiusOf(unit);
                if (radius <= 0f) continue;

                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    Position pos = model.Position;
                    // Default-constructed Position is the unplaced sentinel; never radiate from it.
                    if (pos.x == 0f && pos.z == 0f) continue;
                    (discs ??= new List<PlacementDisc>()).Add(new PlacementDisc(pos, radius));
                }
            }

            return (IReadOnlyList<PlacementDisc>?)discs ?? Array.Empty<PlacementDisc>();
        }
    }
}
