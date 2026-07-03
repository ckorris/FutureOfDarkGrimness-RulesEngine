using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace FDG.Tests
{
    // #150: a movement path's per-waypoint facing defaults to the direction of travel (so a model turns
    // through corners), a manual override locks it, and a zero-length hold keeps the prior facing.
    [TestFixture]
    public class MovementFacingTests
    {
        [Test]
        public void WaypointFacings_TurnThroughCorner_FacesEachTravelDirection()
        {
            var start = new Position(0f, 0f);
            var waypoints = new List<Position> { new Position(0f, 3f), new Position(3f, 3f) };

            var facings = MovementFacingUtilities.WaypointFacings(start, waypoints, new Float2(0f, 1f), 0f);

            Assert.That(facings.Count, Is.EqualTo(2));
            Assert.That(facings[0].X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(facings[0].Y, Is.EqualTo(1f).Within(1e-4f), "moved +Z → faces +Z");
            Assert.That(facings[1].X, Is.EqualTo(1f).Within(1e-4f), "then moved +X → faces +X");
            Assert.That(facings[1].Y, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void WaypointFacings_Offset_RotatesEachTravelDirection()
        {
            var start = new Position(0f, 0f);
            var waypoints = new List<Position> { new Position(0f, 3f), new Position(3f, 3f) };

            // A +90° manual offset: each waypoint's travel direction rotated 90° (not a fixed lock).
            var facings = MovementFacingUtilities.WaypointFacings(start, waypoints, new Float2(0f, 1f), MathF.PI / 2f);

            // travel +Z (0,1) rotated +90° → (-1,0); travel +X (1,0) rotated +90° → (0,1).
            Assert.That(facings[0].X, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(facings[0].Y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(facings[1].X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(facings[1].Y, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void WaypointFacings_ZeroLengthHold_KeepsPriorFacing()
        {
            var start = new Position(0f, 0f);
            // +X to (2,0), then a hold at (2,0), then +Z to (2,2).
            var waypoints = new List<Position> { new Position(2f, 0f), new Position(2f, 0f), new Position(2f, 2f) };

            var facings = MovementFacingUtilities.WaypointFacings(start, waypoints, new Float2(0f, 1f), 0f);

            Assert.That(facings[0].X, Is.EqualTo(1f).Within(1e-4f), "moved +X");
            Assert.That(facings[1].X, Is.EqualTo(1f).Within(1e-4f), "hold keeps the prior +X facing");
            Assert.That(facings[1].Y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(facings[2].Y, Is.EqualTo(1f).Within(1e-4f), "then moved +Z");
        }
    }
}
