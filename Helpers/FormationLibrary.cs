using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// The single home for "arrange these models into rows" geometry (#277). Generates the candidate
    /// row partitions a unit can form up in (line, 5x2, 4-3-3, ...), lays any partition out from
    /// per-model footprint extents, and filters the catalog to shapes that satisfy the 9" all-pairs
    /// coherency span. CohesiveFormation.PackGrid and the GUI group placement/movement resolvers all
    /// lay rows out through <see cref="LayoutOffsets"/>, so row spacing / cohesion conventions live in
    /// exactly one place. All values are inches on the x/z plane; row 0 is the FRONT row (+z side),
    /// rows stack toward -z, and offsets are measured from the formation centroid.
    /// </summary>
    public static class FormationLibrary
    {
        /// <summary>A candidate formation shape: row sizes front-to-back plus a display name.</summary>
        public readonly struct Formation
        {
            /// <summary>Models per row, front row first. Sums to the unit's model count.</summary>
            public readonly int[] RowCounts;
            /// <summary>Display name: "line (10)", "5x2", "4-3-3". ASCII only (game-text rule).</summary>
            public readonly string Name;

            public Formation(int[] rowCounts, string name)
            {
                RowCounts = rowCounts;
                Name = name;
            }
        }

        /// <summary>
        /// The candidate row partitions for <paramref name="count"/> models, shallowest first: the
        /// single line, then balanced splits into 2, 3, ... rows down to rows of pairs. Rows within a
        /// partition differ by at most one model (longer rows first, i.e. at the front). Partitions
        /// that would leave a row with a single model are skipped — a lone model has no in-row
        /// neighbour for the 1" rule, which mixed base sizes can't always bridge cross-row (#159).
        /// </summary>
        public static List<int[]> RowPartitions(int count)
        {
            var partitions = new List<int[]>();
            if (count <= 0) return partitions;

            for (int rows = 1; rows <= Math.Max(1, count / 2); rows++)
            {
                int baseSize = count / rows, remainder = count % rows;
                if (rows > 1 && baseSize < 2) break; // rows would drop below pairs from here on
                var rowCounts = new int[rows];
                for (int r = 0; r < rows; r++) rowCounts[r] = baseSize + (r < remainder ? 1 : 0);
                partitions.Add(rowCounts);
            }
            return partitions;
        }

        /// <summary>Display name for a partition: "line (10)" for one row, "5x2" when all rows are
        /// equal, otherwise "4-3-3". ASCII only.</summary>
        public static string Describe(IReadOnlyList<int> rowCounts)
        {
            if (rowCounts.Count == 1) return $"line ({rowCounts[0]})";
            bool allEqual = true;
            for (int r = 1; r < rowCounts.Count; r++)
                if (rowCounts[r] != rowCounts[0]) { allEqual = false; break; }
            if (allEqual) return $"{rowCounts[0]}x{rowCounts.Count}";
            return string.Join("-", rowCounts);
        }

        /// <summary>
        /// Lays models out in the given rows and returns each model's offset from the formation
        /// centroid. Index k fills the rows in order (row 0 gets indices 0..RowCounts[0]-1 left to
        /// right). Within a row the bases sit edge-to-edge <paramref name="gap"/>" apart at their OWN
        /// X extent; rows are stacked by their tallest model's Z extent + <paramref name="gap"/> — the
        /// same per-model spacing PackGrid uses, so any base-size mix satisfies the 1" rule (#159).
        /// </summary>
        public static (float dx, float dz)[] LayoutOffsets(
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs, IReadOnlyList<int> rowCounts, float gap)
        {
            int n = halfXs.Count;
            var offsets = new (float dx, float dz)[n];
            if (n == 0) return offsets;

            int rows = rowCounts.Count;
            var rowHalfHeight = new float[rows];
            var rowWidth = new float[rows];
            int idx = 0;
            for (int row = 0; row < rows; row++)
            {
                float maxHalfZ = 0f, width = 0f;
                for (int c = 0; c < rowCounts[row]; c++, idx++)
                {
                    if (halfZs[idx] > maxHalfZ) maxHalfZ = halfZs[idx];
                    width += 2f * halfXs[idx];
                }
                rowHalfHeight[row] = maxHalfZ;
                rowWidth[row] = width + (rowCounts[row] - 1) * gap;
            }

            float totalHeight = (rows - 1) * gap;
            for (int row = 0; row < rows; row++) totalHeight += 2f * rowHalfHeight[row];

            idx = 0;
            float zCursor = totalHeight / 2f; // front row at the top (+z), stacking downward
            for (int row = 0; row < rows; row++)
            {
                float rowCenterZ = zCursor - rowHalfHeight[row];
                float xCursor = -rowWidth[row] / 2f;
                for (int c = 0; c < rowCounts[row]; c++, idx++)
                {
                    offsets[idx] = (xCursor + halfXs[idx], rowCenterZ);
                    xCursor += 2f * halfXs[idx] + gap;
                }
                zCursor -= 2f * rowHalfHeight[row] + gap;
            }

            Recenter(offsets);
            return offsets;
        }

        /// <summary>
        /// The formations this unit may legally adopt: every <see cref="RowPartitions"/> candidate whose
        /// laid-out shape keeps all pairs within <paramref name="maxPairwiseInches"/> base-to-base
        /// (approximated by the circumscribing <paramref name="radii"/> — conservative for rectangles).
        /// A 10-model line that would span more than 9" simply isn't offered. The 1" nearest-neighbour
        /// rule is satisfied by construction (rows pack at <paramref name="gap"/>").
        /// </summary>
        public static List<Formation> LegalFormations(
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs, IReadOnlyList<float> radii,
            float gap, float maxPairwiseInches)
        {
            var legal = new List<Formation>();
            foreach (int[] rowCounts in RowPartitions(halfXs.Count))
            {
                var offsets = LayoutOffsets(halfXs, halfZs, rowCounts, gap);
                if (MaxPairwiseBaseToBase(offsets, radii) <= maxPairwiseInches)
                    legal.Add(new Formation(rowCounts, Describe(rowCounts)));
            }
            return legal;
        }

        /// <summary>
        /// Lays the partition out AND assigns each model the slot nearest to where it currently
        /// stands, so per-model travel into the new shape stays small (the same greedy idea as
        /// CohesiveFormation.AssignModelsToCells). Returns each MODEL's offset from the formation
        /// centroid, in the input model order. Extents are permuted with the assignment before the
        /// final layout, so a mixed-base unit still gets per-model row spacing. For unplaced models
        /// (all at the same point) the assignment degenerates to input order.
        /// </summary>
        public static (float dx, float dz)[] PlanFormationOffsets(
            IReadOnlyList<Position> currentPositions,
            IReadOnlyList<float> halfXs, IReadOnlyList<float> halfZs,
            IReadOnlyList<int> rowCounts, float gap)
        {
            int n = halfXs.Count;
            if (n == 0) return Array.Empty<(float, float)>();

            // Provisional slots (input-order extents) around the unit's current centroid decide who
            // goes where; the final layout re-spaces with the assigned models' own extents.
            var provisional = LayoutOffsets(halfXs, halfZs, rowCounts, gap);
            float cx = 0f, cz = 0f;
            for (int i = 0; i < n; i++) { cx += currentPositions[i].x; cz += currentPositions[i].z; }
            cx /= n; cz /= n;

            var modelForSlot = new int[n];
            var used = new bool[n];
            for (int k = 0; k < n; k++)
            {
                float slotX = cx + provisional[k].dx, slotZ = cz + provisional[k].dz;
                int best = -1;
                float bestDist = float.MaxValue;
                for (int m = 0; m < n; m++)
                {
                    if (used[m]) continue;
                    float dx = currentPositions[m].x - slotX, dz = currentPositions[m].z - slotZ;
                    float d = dx * dx + dz * dz;
                    if (d < bestDist) { bestDist = d; best = m; }
                }
                used[best] = true;
                modelForSlot[k] = best;
            }

            var permutedHalfXs = new float[n];
            var permutedHalfZs = new float[n];
            for (int k = 0; k < n; k++)
            {
                permutedHalfXs[k] = halfXs[modelForSlot[k]];
                permutedHalfZs[k] = halfZs[modelForSlot[k]];
            }
            var slotOffsets = LayoutOffsets(permutedHalfXs, permutedHalfZs, rowCounts, gap);

            var byModel = new (float dx, float dz)[n];
            for (int k = 0; k < n; k++) byModel[modelForSlot[k]] = slotOffsets[k];
            return byModel;
        }

        /// <summary>Largest base-to-base distance between any two laid-out models, by circumscribing radii.</summary>
        private static float MaxPairwiseBaseToBase((float dx, float dz)[] offsets, IReadOnlyList<float> radii)
        {
            float max = 0f;
            for (int i = 0; i < offsets.Length; i++)
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    float dx = offsets[i].dx - offsets[j].dx, dz = offsets[i].dz - offsets[j].dz;
                    float b2b = MathF.Sqrt(dx * dx + dz * dz) - radii[i] - radii[j];
                    if (b2b > max) max = b2b;
                }
            return max;
        }

        /// <summary>Shifts all offsets so the formation centroid sits at (0,0).</summary>
        private static void Recenter((float dx, float dz)[] offsets)
        {
            if (offsets.Length == 0) return;
            float sx = 0f, sz = 0f;
            for (int i = 0; i < offsets.Length; i++) { sx += offsets[i].dx; sz += offsets[i].dz; }
            float cx = sx / offsets.Length, cz = sz / offsets.Length;
            for (int i = 0; i < offsets.Length; i++) offsets[i] = (offsets[i].dx - cx, offsets[i].dz - cz);
        }
    }
}
