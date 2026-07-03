using NUnit.Framework;

namespace FDG.Tests
{
    // #150 slice 3: terrain swept-path checks measure a model's true (oriented) base footprint swept along its
    // path, not the circumscribing circle. SAT-based, so a long rectangle blocks where its inscribed bounding
    // circle would miss, and rotating the base changes the outcome.
    [TestFixture]
    public class SweptBaseGeometryTests
    {
        [Test]
        public void Rectangle_LongAxisReachesTerrain_WhereBoundingCircleWouldMiss()
        {
            // Terrain band at Z 2.5–4; a 1"×6" base at the origin (0.5" half-width, 3" half-height).
            var terrain = new RectangularZone(-1f, 1f, 2.5f, 4f);
            var rect = new RectangleBase(1f, 6f);
            var at = new Float2(0f, 0f);

            // Facing +Z → the 3" half-height reaches Z=3, inside the terrain → intersects.
            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(terrain, at, at, rect, new Float2(0f, 1f)), Is.True);

            // The inscribed bounding circle (r=0.5) reaches only Z=0.5 — the pre-#150 approximation would miss it.
            Assert.That(terrain.DoesPathIntersectZone(at, at, rect.BoundingRadiusInches), Is.False);

            // Facing +X → only the 0.5" half-width points at the terrain (reaches Z=0.5) → no intersection.
            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(terrain, at, at, rect, new Float2(1f, 0f)), Is.False);
        }

        [Test]
        public void Rectangle_SweepCarriesFootprintThroughTerrain()
        {
            // Terrain band at X 4–6; a 2"×1" base (1" half-width along X).
            var terrain = new RectangularZone(4f, 6f, -1f, 1f);
            var rect = new RectangleBase(2f, 1f);
            var start = new Float2(0f, 0f);
            var end = new Float2(3f, 0f);

            // The centre stops at X=3, but the 1" half-width reaches X=4 (the terrain edge) → intersects.
            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(terrain, start, end, rect, new Float2(0f, 1f)), Is.True);

            // A bare centre path (no footprint) stops at X=3, short of the X=4 terrain → misses.
            Assert.That(terrain.DoesPathIntersectZone(start, end), Is.False);
        }

        [Test]
        public void Circle_MatchesScalarSweptDisc()
        {
            var terrain = new RectangularZone(0f, 10f, 0f, 10f);
            var start = new Float2(-5f, 5f);
            var end = new Float2(-2f, 5f); // ends 2" short of the zone; a 1" disc still misses
            var circle = new CircleBase(1f);

            Assert.That(
                SweptBaseGeometry.DoesSweptBaseIntersectZone(terrain, start, end, circle, new Float2(0f, 1f)),
                Is.EqualTo(terrain.DoesPathIntersectZone(start, end, 1f)),
                "a circular base must match the existing scalar swept-disc exactly.");
        }

        // #155: entry-distance bisection used by the difficult-terrain move-preview clamp.

        [Test]
        public void MaxTravel_StopsWhereDiscTouchesZoneEdge()
        {
            // Zone at X 4–6; a 1" disc travelling +X from (0,5) touches the X=4 edge when its centre is at 3.
            var zone = new RectangularZone(4f, 6f, 3f, 7f);
            float travel = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                zone, new Float2(0f, 5f), new Float2(10f, 5f), new CircleBase(1f), new Float2(0f, 1f));

            Assert.That(travel, Is.EqualTo(3f).Within(0.02f));
            // The returned distance must itself be clear — it's the bisection's known-good bound.
            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(
                zone, new Float2(0f, 5f), new Float2(travel, 5f), new CircleBase(1f), new Float2(0f, 1f)), Is.False);
        }

        [Test]
        public void MaxTravel_SegmentNeverReachesZone_ReturnsFullLength()
        {
            var zone = new RectangularZone(4f, 6f, 3f, 7f);
            float travel = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                zone, new Float2(0f, 5f), new Float2(2f, 5f), new CircleBase(1f), new Float2(0f, 1f));

            Assert.That(travel, Is.EqualTo(2f));
        }

        [Test]
        public void MaxTravel_StartAlreadyTouching_ReturnsZero()
        {
            var zone = new RectangularZone(4f, 6f, 3f, 7f);
            float travel = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                zone, new Float2(5f, 5f), new Float2(20f, 5f), new CircleBase(1f), new Float2(0f, 1f));

            Assert.That(travel, Is.EqualTo(0f));
        }

        [Test]
        public void MaxTravel_RectangleBase_LongAxisShortensEntry()
        {
            // Zone at X 4–6. A 1"x6" base facing +X sweeps its 3" half-height ahead of the centre, so it
            // touches the zone at travel 1; the same base facing +Z leads with its 0.5" half-width (travel 3.5).
            var zone = new RectangularZone(4f, 6f, -10f, 10f);
            var rect = new RectangleBase(1f, 6f);

            float facingTravel = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                zone, new Float2(0f, 0f), new Float2(10f, 0f), rect, new Float2(1f, 0f));
            float sideTravel = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                zone, new Float2(0f, 0f), new Float2(10f, 0f), rect, new Float2(0f, 1f));

            Assert.That(facingTravel, Is.EqualTo(1f).Within(0.02f));
            Assert.That(sideTravel, Is.EqualTo(3.5f).Within(0.02f));
        }

        [Test]
        public void Rectangle_AgainstRotatedZone_OBB()
        {
            // A 4×4 zone rotated 45° about its centre (5,5) — a diamond reaching ~2.83" along the axes.
            var diamond = new RotatedZoneWrapper(new RectangularZone(3f, 7f, 3f, 7f), 45f, new Float2(5f, 5f));
            var rect = new RectangleBase(1f, 1f);

            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(
                diamond, new Float2(5f, 5f), new Float2(5f, 5f), rect, new Float2(0f, 1f)), Is.True,
                "a base at the diamond's centre overlaps it.");

            Assert.That(SweptBaseGeometry.DoesSweptBaseIntersectZone(
                diamond, new Float2(12f, 5f), new Float2(12f, 5f), rect, new Float2(0f, 1f)), Is.False,
                "a base well outside the diamond does not.");
        }
    }
}
