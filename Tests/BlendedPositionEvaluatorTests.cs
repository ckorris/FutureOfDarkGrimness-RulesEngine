using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>#191 C4: the blend arm is arithmetic over two evaluators; pin the arithmetic and the contract.</summary>
    [TestFixture]
    public class BlendedPositionEvaluatorTests
    {
        private sealed class Fixed : IPositionEvaluator
        {
            private readonly SideValues _values;
            public Fixed(params float[] values) => _values = new SideValues(values);
            public SideValues Evaluate(ITableState state, RuleEvaluator evaluator, SideMap sides) => _values.Clone();
        }

        private static (TableState State, RuleEvaluator Evaluator, SideMap Sides) TwoSideBoard()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var us = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            return (new TableState(store), new RuleEvaluator(new ProbabilisticDiceRoller()),
                SideMap.FromSlots(new[] { (us, 0), (them, 1) }));
        }

        [Test]
        public void Blend_IsTheConvexMix_PerSide()
        {
            (TableState state, RuleEvaluator evaluator, SideMap sides) = TwoSideBoard();
            var blend = new BlendedPositionEvaluator(new Fixed(0.8f, 0.2f), new Fixed(0.4f, 0.6f), 0.25f);

            SideValues values = blend.Evaluate(state, evaluator, sides);

            Assert.That(values[0], Is.EqualTo(0.25f * 0.8f + 0.75f * 0.4f).Within(1e-6f));
            Assert.That(values[1], Is.EqualTo(0.25f * 0.2f + 0.75f * 0.6f).Within(1e-6f));
            Assert.That(values.IsComplementaryTwoSide(), Is.True,
                "two complementary halves must blend to a complementary value - the mix is linear");
        }

        [Test]
        public void Weight_Endpoints_ReturnExactlyOneHalf()
        {
            (TableState state, RuleEvaluator evaluator, SideMap sides) = TwoSideBoard();
            var a = new Fixed(0.9f, 0.1f);
            var b = new Fixed(0.3f, 0.7f);

            Assert.That(new BlendedPositionEvaluator(a, b, 1f).Evaluate(state, evaluator, sides)[0], Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(new BlendedPositionEvaluator(a, b, 0f).Evaluate(state, evaluator, sides)[0], Is.EqualTo(0.3f).Within(1e-6f));
        }

        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        public void Weight_OutsideUnitInterval_IsRefused(float weight)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BlendedPositionEvaluator(new Fixed(0.5f, 0.5f), new Fixed(0.5f, 0.5f), weight));
        }

        [Test]
        public void Halves_WithDifferentSideCounts_Throw()
        {
            (TableState state, RuleEvaluator evaluator, SideMap sides) = TwoSideBoard();
            var blend = new BlendedPositionEvaluator(new Fixed(0.5f, 0.5f), new Fixed(0.3f, 0.3f, 0.4f), 0.5f);
            Assert.Throws<InvalidOperationException>(() => blend.Evaluate(state, evaluator, sides));
        }
    }
}
