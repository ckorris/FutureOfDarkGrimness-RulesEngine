using System.Collections.Generic;
using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #149 slice C: model-to-model collision (here, the melee-range gate every charge/strike check flows
    // through) measures with the real base SHAPE, not the circumscribing circle. Proves a non-circular
    // base changes the outcome — a narrow rectangle is out of melee range at a separation where its
    // bounding circle would (wrongly) be in range.
    [TestFixture]
    public class BaseShapeCollisionTests
    {
        [Test]
        public void MeleeRange_NarrowRectangle_OutOfRange_WhereBoundingCircleWouldBeInRange()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();

            // 1" wide × 4" tall bases, 5" apart along the narrow (X) axis: real edge-to-edge gap is 4" —
            // outside the 2" melee range. The circumscribing circle (radius ≈ 2.06") would put them ~0.88"
            // apart, i.e. inside 2" — the approximation the shape-aware path must NOT use.
            IModel rectA = ModelAt(store, new RectangleBase(1f, 4f), new Position(0f, 0f));
            IModel rectB = ModelAt(store, new RectangleBase(1f, 4f), new Position(5f, 0f));
            Assert.That(MeleeRangeUtilities.AreModelsInMeleeRange(rectA, rectB), Is.False,
                "narrow rectangles 4\" edge-to-edge are not in 2\" melee range.");

            // Same centres, but circular bases of the rectangle's bounding radius → would be in range.
            float boundingR = rectA.BaseShape.BoundingRadiusInches;
            IModel circA = ModelAt(store, new CircleBase(boundingR), new Position(0f, 0f));
            IModel circB = ModelAt(store, new CircleBase(boundingR), new Position(5f, 0f));
            Assert.That(MeleeRangeUtilities.AreModelsInMeleeRange(circA, circB), Is.True,
                "the bounding-circle approximation would have called them engaged — the bug shape-awareness fixes.");
        }

        private static IModel ModelAt(GameDataStore store, IBaseShape shape, Position pos)
        {
            ModelData model = new ModelData(shape, new List<Weapon>(), pos, store);
            store.Create(model);
            return model;
        }
    }
}
