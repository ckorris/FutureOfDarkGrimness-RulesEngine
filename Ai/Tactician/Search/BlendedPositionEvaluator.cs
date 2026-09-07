using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// #191 C4 (plan sec 10): the "blend" arm of the integration slice - a convex mix of two leaf
    /// evaluators, <c>weight * primary + (1 - weight) * secondary</c> per side. The mix is linear,
    /// so every property both halves have survives it: two complementary two-side values blend to a
    /// complementary one, values in [0, 1] stay in [0, 1], and an n-side normalization is preserved.
    /// Cost is the sum of the halves (hand ~1.8 ms + MLP ~2.5 us, i.e. the hand evaluator's cost).
    /// </summary>
    public sealed class BlendedPositionEvaluator : IPositionEvaluator
    {
        private readonly IPositionEvaluator _primary;
        private readonly IPositionEvaluator _secondary;
        private readonly float _weight;

        public float Weight => _weight;

        public BlendedPositionEvaluator(IPositionEvaluator primary, IPositionEvaluator secondary, float weight)
        {
            if (!(weight >= 0f && weight <= 1f))
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Blend weight must be in [0, 1].");
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _secondary = secondary ?? throw new ArgumentNullException(nameof(secondary));
            _weight = weight;
        }

        public SideValues Evaluate(ITableState state, RuleEvaluator evaluator, SideMap sides)
        {
            SideValues a = _primary.Evaluate(state, evaluator, sides);
            SideValues b = _secondary.Evaluate(state, evaluator, sides);
            if (a.Count != b.Count)
                throw new InvalidOperationException(
                    $"BlendedPositionEvaluator: halves disagree on side count ({a.Count} vs {b.Count}).");
            var result = new SideValues(a.Count);
            for (int side = 0; side < a.Count; side++)
                result[side] = _weight * a[side] + (1f - _weight) * b[side];
            return result;
        }
    }
}
