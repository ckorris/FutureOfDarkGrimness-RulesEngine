using NUnit.Framework;

namespace FDG.Tests
{
    // IZone.GetLastSegmentExit (#201): the largest-t point where a segment leaves the zone.
    // Contract pinned here: null when the segment never intersects the zone AND null when the
    // end point is inside it (no final exit) — the cover proximity rules rely on the latter so
    // the attacker-exit rule never fires when the target stands inside the cover piece.
    [TestFixture]
    public class SegmentExitTests
    {
        private const float Tol = 0.001f;

        private static void AssertExit(Float2? exit, float x, float y)
        {
            Assert.That(exit, Is.Not.Null);
            Assert.That(exit!.Value.X, Is.EqualTo(x).Within(Tol));
            Assert.That(exit.Value.Y, Is.EqualTo(y).Within(Tol));
        }

        // ---- RectangularZone ----

        [Test]
        public void Rect_CleanCrossing_ExitsFarSide()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            AssertExit(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(20, 5)), 12, 5);
        }

        [Test]
        public void Rect_StartInside_ExitStillFound()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            AssertExit(zone.GetLastSegmentExit(new Float2(10, 5), new Float2(20, 5)), 12, 5);
        }

        [Test]
        public void Rect_EndInside_Null()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(10, 5)), Is.Null);
        }

        [Test]
        public void Rect_NoIntersection_Null()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 15), new Float2(20, 15)), Is.Null);
        }

        [Test]
        public void Rect_DegenerateSegmentOutside_Null()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(0, 5)), Is.Null);
        }

        [Test]
        public void Rect_ObliqueCrossing_ExitOnCorrectEdge()
        {
            var zone = new RectangularZone(8, 12, 3, 7);
            // From below-left through the rect, leaving through the top edge (y = 7) at x = 10.5.
            AssertExit(zone.GetLastSegmentExit(new Float2(7, 0), new Float2(12, 10)), 10.5f, 7);
        }

        // ---- CircularZone ----

        [Test]
        public void Circle_CleanCrossing_ExitsFarSide()
        {
            var zone = new CircularZone(10, 5, 2);
            AssertExit(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(20, 5)), 12, 5);
        }

        [Test]
        public void Circle_StartInside_ExitStillFound()
        {
            var zone = new CircularZone(10, 5, 2);
            AssertExit(zone.GetLastSegmentExit(new Float2(10, 5), new Float2(20, 5)), 12, 5);
        }

        [Test]
        public void Circle_EndInside_Null()
        {
            var zone = new CircularZone(10, 5, 2);
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(10, 5)), Is.Null);
        }

        [Test]
        public void Circle_NoIntersection_Null()
        {
            var zone = new CircularZone(10, 5, 2);
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 10), new Float2(20, 10)), Is.Null);
        }

        [Test]
        public void Circle_Tangent_ReturnsTouchPoint()
        {
            // Zero-depth graze: consistent with DoesPathIntersectZone's inclusive boundary.
            var zone = new CircularZone(10, 5, 2);
            AssertExit(zone.GetLastSegmentExit(new Float2(0, 7), new Float2(20, 7)), 10, 7);
        }

        // ---- RotatedZoneWrapper ----

        [Test]
        public void RotatedRect_ExitMappedBackToWorld()
        {
            // 4x2 rect rotated 90 deg about its own center becomes 2 wide (x 9..11), 4 tall (y 3..7):
            // a horizontal crossing at y=5 exits at x=11 either rotation direction.
            var zone = new RotatedZoneWrapper(new RectangularZone(8, 12, 4, 6), 90f, new Float2(10, 5));
            AssertExit(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(20, 5)), 11, 5);
        }

        [Test]
        public void RotatedRect_EndInside_Null()
        {
            var zone = new RotatedZoneWrapper(new RectangularZone(8, 12, 4, 6), 90f, new Float2(10, 5));
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(10, 5)), Is.Null);
        }

        // ---- CompositeZone ----

        [Test]
        public void Composite_FarPartOwnsLastExit()
        {
            var zone = new CompositeZone(new IZone[]
            {
                new RectangularZone(5, 6, 0, 10),
                new RectangularZone(9, 10, 0, 10),
            });
            AssertExit(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(20, 5)), 10, 5);
        }

        [Test]
        public void Composite_StartInsideNearPart_FarPartStillOwnsLastExit()
        {
            var zone = new CompositeZone(new IZone[]
            {
                new RectangularZone(5, 6, 0, 10),
                new RectangularZone(9, 10, 0, 10),
            });
            AssertExit(zone.GetLastSegmentExit(new Float2(5.5f, 5), new Float2(20, 5)), 10, 5);
        }

        [Test]
        public void Composite_EndInsideAnyPart_Null()
        {
            var zone = new CompositeZone(new IZone[]
            {
                new RectangularZone(5, 6, 0, 10),
                new RectangularZone(9, 10, 0, 10),
            });
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 5), new Float2(9.5f, 5)), Is.Null);
        }

        [Test]
        public void Composite_NoPartCrossed_Null()
        {
            var zone = new CompositeZone(new IZone[]
            {
                new RectangularZone(5, 6, 0, 10),
                new RectangularZone(9, 10, 0, 10),
            });
            Assert.That(zone.GetLastSegmentExit(new Float2(0, 15), new Float2(20, 15)), Is.Null);
        }

        // ---- TerrainData delegation ----

        [Test]
        public void TerrainData_DelegatesToShape()
        {
            var terrain = new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 3, 7));
            AssertExit(terrain.GetLastSegmentExit(new Float2(0, 5), new Float2(20, 5)), 12, 5);
        }
    }
}
