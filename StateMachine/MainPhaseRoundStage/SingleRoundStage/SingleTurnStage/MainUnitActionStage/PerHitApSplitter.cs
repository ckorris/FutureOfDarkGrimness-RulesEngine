using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;

namespace FDG.Stages
{
    /// <summary>
    /// Splits a batch of successful hits into per-AP groups so that Rending / Crack style AP-on-a-6
    /// lands only on the hits that actually rolled the triggering face, not the whole attack. A
    /// <see cref="RuleOperation.ApplyPerHitSaveModifier"/> names a face (an unmodified 6) and a save
    /// delta; the matching-face hits become their own <see cref="SuccessfulHitInfo"/> carrying that
    /// delta (the #016 per-hit save-modifier seam), while every other successful hit stays in a single
    /// base-AP group. Whole-attack save modifiers (Thrust, cover, defensive buffs) still ride
    /// <c>RollToHitResults.SaveModifier</c> and are applied on top of these groups by the save stage.
    ///
    /// Everything is expressed as histogram ops (<see cref="IDiceResults.SubsetAt"/>), so a face's hit
    /// count may be fractional under the probabilistic roller and still splits correctly.
    /// </summary>
    public static class PerHitApSplitter
    {
        /// <summary>
        /// Partitions <paramref name="successfulHits"/> using the per-hit save-modifier operations in
        /// <paramref name="operations"/>. Returns one group per triggering face that actually landed
        /// hits (each carrying its summed save delta) plus one base-AP group for the remainder. Always
        /// returns at least one group — a whiffed volley (0 hits) still yields a single empty group, so
        /// downstream save stages see one entry per volley exactly as before.
        /// </summary>
        public static List<SuccessfulHitInfo> Split(
            IDiceResults successfulHits, IReadOnlyList<RuleOperation> operations)
        {
            // Sum the deltas per triggering face — two AP-on-6 rules (e.g. Rending + Crack) on the same
            // weapon stack onto the same natural-6 hits.
            Dictionary<int, int> deltaByFace = new Dictionary<int, int>();
            foreach (RuleOperation.ApplyPerHitSaveModifier op in
                operations.OfType<RuleOperation.ApplyPerHitSaveModifier>())
            {
                deltaByFace.TryGetValue(op.OnRollValue, out int current);
                deltaByFace[op.OnRollValue] = current + op.Delta;
            }

            if (deltaByFace.Count == 0)
            {
                return new List<SuccessfulHitInfo> { new SuccessfulHitInfo(successfulHits) };
            }

            List<SuccessfulHitInfo> groups = new List<SuccessfulHitInfo>();
            HashSet<int> peeledFaces = new HashSet<int>();

            foreach (KeyValuePair<int, int> entry in deltaByFace)
            {
                int face = entry.Key;
                // A triggering face outside the successful-hit range carried no hits (e.g. a face below
                // the hit threshold is never a hit). Nothing to peel.
                if (face < successfulHits.SideMin || face > successfulHits.SideMax) continue;

                IDiceResults faceHits = successfulHits.SubsetAt(face);
                if (faceHits.TotalRolls > 0f)
                {
                    groups.Add(new SuccessfulHitInfo(faceHits, entry.Value));
                    peeledFaces.Add(face);
                }
            }

            IDiceResults remainder = RemoveFaces(successfulHits, peeledFaces);
            if (remainder.TotalRolls > 0f)
            {
                groups.Add(new SuccessfulHitInfo(remainder));
            }

            // No hits landed anywhere (whiff, or the only hits were on peeled faces with 0 count): keep
            // the one-group-per-volley invariant with the original (possibly empty) batch.
            if (groups.Count == 0)
            {
                groups.Add(new SuccessfulHitInfo(successfulHits));
            }

            return groups;
        }

        // Copies the histogram with the given faces zeroed out — the base-AP remainder after the
        // per-face AP groups have been peeled off.
        private static IDiceResults RemoveFaces(IDiceResults source, ICollection<int> facesToRemove)
        {
            if (facesToRemove.Count == 0) return source;

            int length = source.SideMax - source.SideMin + 1;
            float[] values = new float[length];
            for (int face = source.SideMin; face <= source.SideMax; face++)
            {
                values[face - source.SideMin] = facesToRemove.Contains(face) ? 0f : source.At(face);
            }

            return new DiceResults(values, source.SideMin);
        }
    }
}
