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
            => Split(successfulHits, operations.Select(o => (o, "")).ToList());

        /// <summary>
        /// As <see cref="Split(IDiceResults, IReadOnlyList{RuleOperation})"/>, but each per-hit-AP operation
        /// carries the alias-aware name of the rule that produced it (#204), so the peeled group's
        /// <see cref="HitGroupSource"/> can attribute the raised save threshold to "Rending" / "Crack" in
        /// the save-roll presentation. The base remainder is tagged <see cref="HitGroupSource.Base"/>.
        /// </summary>
        public static List<SuccessfulHitInfo> Split(
            IDiceResults successfulHits, IReadOnlyList<(RuleOperation Op, string RuleName)> namedOperations)
        {
            // Sum the deltas per triggering face — two AP-on-6 rules (e.g. Rending + Crack) on the same
            // weapon stack onto the same natural-6 hits — and remember which rule(s) named each face.
            Dictionary<int, int> deltaByFace = new Dictionary<int, int>();
            Dictionary<int, List<string>> namesByFace = new Dictionary<int, List<string>>();
            foreach ((RuleOperation op, string ruleName) in namedOperations)
            {
                if (op is not RuleOperation.ApplyPerHitSaveModifier perHit) continue;
                deltaByFace.TryGetValue(perHit.OnRollValue, out int current);
                deltaByFace[perHit.OnRollValue] = current + perHit.Delta;
                if (!namesByFace.TryGetValue(perHit.OnRollValue, out List<string>? names))
                    namesByFace[perHit.OnRollValue] = names = new List<string>();
                if (!string.IsNullOrEmpty(ruleName) && !names.Contains(ruleName)) names.Add(ruleName);
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
                    // Amount is the AP magnitude for display ("AP+1"): a negative save delta raises the threshold.
                    string ruleName = namesByFace.TryGetValue(face, out List<string>? ns) && ns.Count > 0
                        ? string.Join("+", ns) : "";
                    var source = new HitGroupSource(EHitSourceKind.PerHitAp, ruleName, -entry.Value);
                    groups.Add(new SuccessfulHitInfo(faceHits, entry.Value, source));
                    peeledFaces.Add(face);
                }
            }

            IDiceResults remainder = RemoveFaces(successfulHits, peeledFaces);
            if (remainder.TotalRolls > 0f)
            {
                groups.Add(new SuccessfulHitInfo(remainder, 0, HitGroupSource.Base));
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
