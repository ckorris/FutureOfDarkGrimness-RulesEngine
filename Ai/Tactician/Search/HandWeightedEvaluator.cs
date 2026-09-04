using FDG.Ai.Tactician.Learning;
using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// B3 (#191 campaign step 7): a hand-weighted linear combination of the C1
    /// <see cref="PositionEncoder"/> vector, one side's per-side block at a time, mapped to a
    /// win-probability-shaped value in [0, 1]. This is B's real leaf evaluator - C3 (step 14)
    /// replaces the weights with a trained net and touches nothing else in the search (the doc's
    /// "B and C share one code path").
    /// <para>
    /// Weight order, per the campaign doc's step 7 spec: objective share dominant (CLAUDE.md -
    /// "objectives decide the winner"), then value share (a side's remaining fighting capacity),
    /// then threat coverage (a coarse, target-independent positional proxy - schema doc sec 3 notes
    /// its own precision is already traded away for the 5ms budget, so it earns the smallest say).
    /// Weights sum to 1 so the raw combination is itself in [0,1] without a clamp; every input share
    /// (<see cref="PositionEncoder.EncodeSideBlock"/> indices 6/1/11) is already in [0,1].
    /// </para>
    /// <para>
    /// Two-side complementarity (sec 7.2) uses <see cref="ObjectiveShareEvaluator"/>'s own proven
    /// shape - 0.5 + (own raw - best other raw) / 2 - which sums to exactly 1 for two sides with no
    /// clamp ever engaging (raw in [0,1] => the difference is in [-1,1] => the halved, offset result
    /// is already in [0,1]), rather than re-deriving a new normalization.
    /// </para>
    /// </summary>
    public sealed class HandWeightedEvaluator : IPositionEvaluator
    {
        private const float ObjectiveWeight = 0.55f;
        private const float ValueWeight = 0.30f;
        private const float ThreatWeight = 0.15f;

        public SideValues Evaluate(ITableState state, RuleEvaluator evaluator, SideMap sides)
        {
            var membersBySide = new List<PlayerID>[sides.Count];
            for (int side = 0; side < sides.Count; side++) membersBySide[side] = new List<PlayerID>();
            foreach (PlayerID player in sides.Players) membersBySide[sides.SideOf(player)].Add(player);

            var raw = new float[sides.Count];
            for (int side = 0; side < sides.Count; side++)
            {
                var opposing = new List<PlayerID>();
                for (int other = 0; other < sides.Count; other++)
                    if (other != side) opposing.AddRange(membersBySide[other]);

                float[] block = PositionEncoder.EncodeSideBlock(state, evaluator, membersBySide[side], opposing);
                float objHeldShare = block[6];
                float valueShare = block[1];
                float threatCoverage = block[11];
                raw[side] = Math.Clamp(
                    ObjectiveWeight * objHeldShare + ValueWeight * valueShare + ThreatWeight * threatCoverage,
                    0f, 1f);
            }

            var values = new SideValues(sides.Count);
            for (int side = 0; side < sides.Count; side++)
            {
                float bestOther = 0f;
                for (int other = 0; other < sides.Count; other++)
                    if (other != side) bestOther = Math.Max(bestOther, raw[other]);
                values[side] = 0.5f + (raw[side] - bestOther) / 2f;
            }
            return values;
        }
    }
}
