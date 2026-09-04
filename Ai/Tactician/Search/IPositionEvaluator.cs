namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// The leaf value seam (#191 B2 sec 7.2; the campaign's step 7 / B3 fills it, step 14's ONNX
    /// evaluator lands behind the same interface). One value per side, each in [0, 1].
    /// <para>
    /// Contract: a PURE read of <paramref name="state"/> - never a roll, never a mutation - because it
    /// runs on the live store of a simulated game at the boundary the line stops at (sec 5.2), before
    /// that line's one Save. With exactly two sides the result must satisfy v[other] == 1 - v[self]
    /// (<see cref="SideValues.IsComplementaryTwoSide"/>), which is what reduces max^n to minimax in
    /// 1v1; a test asserts it for every shipped evaluator.
    /// </para>
    /// </summary>
    public interface IPositionEvaluator
    {
        SideValues Evaluate(ITableState state, SideMap sides);
    }

    /// <summary>
    /// 0.5 for every side at every non-terminal leaf: the tree is then driven by terminals and by the
    /// priors alone. B2's placeholder; useful as the control arm when measuring what an evaluator adds.
    /// </summary>
    public sealed class TerminalOnlyEvaluator : IPositionEvaluator
    {
        public SideValues Evaluate(ITableState state, SideMap sides) => SideValues.Uniform(sides.Count, 0.5f);
    }

    /// <summary>
    /// Each side's projected objective lead, as a value in [0, 1]: 0.5 + (own - best other) / (2 x
    /// objective count), clipped. With two sides the two values sum to exactly 1. A placeholder so
    /// end-to-end tests have a gradient (sec 7.2); B3 replaces it with the C1-vector evaluator.
    /// Objectives decide games (CLAUDE.md), so this is also the least wrong single scalar.
    /// </summary>
    public sealed class ObjectiveShareEvaluator : IPositionEvaluator
    {
        public SideValues Evaluate(ITableState state, SideMap sides)
        {
            List<ObjectiveProjection> projections = TacticalAnalysis.ProjectObjectives(state);
            int total = projections.Count;
            if (total == 0) return SideValues.Uniform(sides.Count, 0.5f);

            var owned = new int[sides.Count];
            foreach (ObjectiveProjection projection in projections)
            {
                if (projection.ProjectedOwner is { } owner) owned[sides.SideOf(owner)]++;
            }

            var values = new SideValues(sides.Count);
            for (int side = 0; side < sides.Count; side++)
            {
                int bestOther = 0;
                for (int other = 0; other < sides.Count; other++)
                    if (other != side && owned[other] > bestOther) bestOther = owned[other];
                float value = 0.5f + (owned[side] - bestOther) / (2f * total);
                values[side] = Math.Clamp(value, 0f, 1f);
            }
            return values;
        }
    }
}
