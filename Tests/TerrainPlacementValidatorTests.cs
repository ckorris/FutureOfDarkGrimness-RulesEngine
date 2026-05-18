using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class TerrainPlacementValidatorTests
    {
        private const float TableW = 72f;
        private const float TableH = 48f;

        private static ITerrain Rect(float l, float r, float b, float t) =>
            new TerrainData(ETerrainType.Cover, new RectangularZone(l, r, b, t));

        private static ITerrain Circle(float x, float y, float radius) =>
            new TerrainData(ETerrainType.Cover, new CircularZone(x, y, radius));

        [Test]
        public void Rect_FullyInside_Valid()
        {
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(10, 20, 10, 20), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.Valid));
        }

        [Test]
        public void Rect_OffLeftEdge_OutOfBounds()
        {
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(-1, 5, 10, 20), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OutOfBounds));
        }

        [Test]
        public void Rect_OffRightEdge_OutOfBounds()
        {
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(70, 73, 10, 20), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OutOfBounds));
        }

        [Test]
        public void Rect_OffTopEdge_OutOfBounds()
        {
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(10, 20, 40, 49), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OutOfBounds));
        }

        [Test]
        public void Circle_PartiallyOffEdge_OutOfBounds()
        {
            var result = TerrainPlacementValidator.Check(
                new CircularZone(2, 24, 3), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OutOfBounds));
        }

        [Test]
        public void Circle_FullyInside_Valid()
        {
            var result = TerrainPlacementValidator.Check(
                new CircularZone(20, 20, 3), TableW, TableH, Array.Empty<ITerrain>());
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.Valid));
        }

        [Test]
        public void RectVsRect_Overlapping_Rejected()
        {
            var existing = new[] { Rect(10, 20, 10, 20) };
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(15, 25, 15, 25), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OverlapsExistingTerrain));
        }

        [Test]
        public void RectVsRect_Touching_Rejected()
        {
            // Edges share x=20: with the GapMarginInches strictness this counts as overlap.
            var existing = new[] { Rect(10, 20, 10, 20) };
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(20, 30, 10, 20), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OverlapsExistingTerrain));
        }

        [Test]
        public void RectVsRect_WellSeparated_Valid()
        {
            var existing = new[] { Rect(10, 20, 10, 20) };
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(22, 30, 10, 20), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.Valid));
        }

        [Test]
        public void CircleVsCircle_Overlapping_Rejected()
        {
            var existing = new[] { Circle(20, 20, 5) };
            var result = TerrainPlacementValidator.Check(
                new CircularZone(28, 20, 5), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OverlapsExistingTerrain));
        }

        [Test]
        public void CircleVsCircle_WellSeparated_Valid()
        {
            var existing = new[] { Circle(20, 20, 5) };
            var result = TerrainPlacementValidator.Check(
                new CircularZone(32, 20, 5), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.Valid));
        }

        [Test]
        public void RectVsCircle_CornerInsideCircle_Rejected()
        {
            var existing = new[] { Circle(15, 15, 5) };
            // Rect at (18,28)x(18,28). Closest point on rect to circle center (15,15) is (18,18); distance sqrt(18)≈4.24 < 5.
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(18, 28, 18, 28), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OverlapsExistingTerrain));
        }

        [Test]
        public void RectVsCircle_Separated_Valid()
        {
            var existing = new[] { Circle(15, 15, 5) };
            var result = TerrainPlacementValidator.Check(
                new RectangularZone(25, 35, 25, 35), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.Valid));
        }

        [Test]
        public void CircleVsRect_Symmetric()
        {
            // Same configuration as RectVsCircle_CornerInsideCircle, swapped roles.
            var existing = new[] { Rect(18, 28, 18, 28) };
            var result = TerrainPlacementValidator.Check(
                new CircularZone(15, 15, 5), TableW, TableH, existing);
            Assert.That(result, Is.EqualTo(TerrainPlacementValidity.OverlapsExistingTerrain));
        }
    }
}
