using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class LineOfSightTests
    {
        private static readonly Position Attacker = new Position(0, 5);
        private static readonly Position Target   = new Position(20, 5);

        [Test]
        public void NoTerrain_IsClear()
        {
            ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain: null);
            Assert.That(effect, Is.EqualTo(ESightLineEffect.Clear));
            Assert.That(LineOfSightUtilities.HasLineOfSight(Attacker, Target, terrain: null), Is.True);
        }

        [Test]
        public void EmptyTerrain_IsClear()
        {
            ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(Attacker, Target, new List<ITerrain>());
            Assert.That(effect, Is.EqualTo(ESightLineEffect.Clear));
        }

        [Test]
        public void BlockingRectangleBetween_IsBlocking()
        {
            //Rect spanning x 8..12, y 3..7 — segment from (0,5)→(20,5) crosses it.
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 3, 7))
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
            Assert.That(LineOfSightUtilities.HasLineOfSight(Attacker, Target, terrain), Is.False);
        }

        [Test]
        public void BlockingCircleBetween_IsBlocking()
        {
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Blocking, new CircularZone(new Float2(10, 5), 2))
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
        }

        [Test]
        public void CoverBetween_IsCover_NotBlocking()
        {
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 3, 7))
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Cover));
            Assert.That(LineOfSightUtilities.HasLineOfSight(Attacker, Target, terrain), Is.True);
        }

        [Test]
        public void TerrainOffToTheSide_IsClear()
        {
            //Rect at y 30..35, far from segment along y=5.
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 30, 35))
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Clear));
        }

        [Test]
        public void NonBlockingNonCoverFlags_DoNotAffectSight()
        {
            //Difficult/Dangerous-only terrain has no sight effect.
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(8, 12, 3, 7)),
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(8, 12, 3, 7)),
                new TerrainData(ETerrainType.Impassible, new RectangularZone(8, 12, 3, 7)),
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Clear),
                "Impassible without Blocking should not block sight (chasm).");
        }

        [Test]
        public void BlockingBeatsCover_OnSameSegment()
        {
            //Cover on the segment AND a separate Blocking on the segment → Blocking wins.
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(2, 6, 3, 7)),
                new TerrainData(ETerrainType.Blocking, new RectangularZone(14, 18, 3, 7)),
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, terrain),
                Is.EqualTo(ESightLineEffect.Blocking));
        }

        [Test]
        public void TargetInsideCover_IsCover()
        {
            //Endpoint-inside-shape counts as intersection (pinned-down behavior of
            //DoesPathIntersectZone), so a defender standing in cover gets cover from
            //the same raycast that handles the "behind cover" case.
            Position targetInForest = new Position(10, 5);
            List<ITerrain> terrain = new()
            {
                new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(10, 5), 3))
            };
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, targetInForest, terrain),
                Is.EqualTo(ESightLineEffect.Cover));
        }

        [Test]
        public void CustomTerrain_OverridesDefault()
        {
            //A door-style terrain that toggles between Blocking and Clear regardless
            //of geometry. Confirms the per-terrain override path works.
            DoorTerrain closedDoor = new(closed: true);
            DoorTerrain openDoor   = new(closed: false);

            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, new[] { closedDoor }),
                Is.EqualTo(ESightLineEffect.Blocking));
            Assert.That(LineOfSightUtilities.EvaluateSightLine(Attacker, Target, new[] { openDoor }),
                Is.EqualTo(ESightLineEffect.Clear));
        }

        [Test]
        public void GetFirstBlockingHit_NoTerrain_ReturnsNull()
        {
            Assert.That(LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, terrain: null), Is.Null);
            Assert.That(LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new List<ITerrain>()), Is.Null);
        }

        [Test]
        public void GetFirstBlockingHit_RectangleOnLine_ReturnsLeftEdge()
        {
            // Rect spans x 8..12, y 3..7. Segment (0,5)→(20,5) enters at x=8.
            var rect = new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 3, 7));
            var result = LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new[] { rect });
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.hit.x, Is.EqualTo(8f).Within(0.001f));
            Assert.That(result.Value.hit.z, Is.EqualTo(5f).Within(0.001f));
            Assert.That(result.Value.piece, Is.SameAs(rect));
        }

        [Test]
        public void GetFirstBlockingHit_CircleOnLine_ReturnsNearEdge()
        {
            // Circle centered (10,5) radius 2. Segment (0,5)→(20,5) enters at x=8.
            var circle = new TerrainData(ETerrainType.Blocking, new CircularZone(10f, 5f, 2f));
            var result = LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new[] { circle });
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.hit.x, Is.EqualTo(8f).Within(0.001f));
            Assert.That(result.Value.hit.z, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void GetFirstBlockingHit_TwoBlockers_ReturnsCloserOne()
        {
            var far  = new TerrainData(ETerrainType.Blocking, new RectangularZone(15, 18, 3, 7));
            var near = new TerrainData(ETerrainType.Blocking, new RectangularZone(5, 8, 3, 7));
            var result = LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new[] { far, near });
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.piece, Is.SameAs(near));
            Assert.That(result.Value.hit.x, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void GetFirstBlockingHit_CoverPieceIgnored()
        {
            // A Cover-only piece on the line must not register as a blocking hit.
            var cover = new TerrainData(ETerrainType.Cover, new RectangularZone(8, 12, 3, 7));
            Assert.That(LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new[] { cover }), Is.Null);
        }

        [Test]
        public void GetFirstBlockingHit_AttackerInsideBlocker_ReturnsAttackerPosition()
        {
            // Attacker is at (0,5) inside a rect spanning x -1..3, y 3..7.
            var enclosing = new TerrainData(ETerrainType.Blocking, new RectangularZone(-1, 3, 3, 7));
            var result = LineOfSightUtilities.GetFirstBlockingHit(Attacker, Target, new[] { enclosing });
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.hit.x, Is.EqualTo(Attacker.x).Within(0.001f));
            Assert.That(result.Value.hit.z, Is.EqualTo(Attacker.z).Within(0.001f));
        }

        private class DoorTerrain : ITerrain
        {
            private readonly bool _closed;
            public DoorTerrain(bool closed) { _closed = closed; }
            public string Name => string.Empty;
            public ETerrainType TerrainType => ETerrainType.Blocking;
            public IZone Shape => new RectangularZone(8, 12, 3, 7);
            public float HeightInches => 4f;
            public bool IsPointWithinZone(Float2 p) => Shape.IsPointWithinZone(p);
            public bool DoesPathIntersectZone(Float2 a, Float2 b) => Shape.DoesPathIntersectZone(a, b);
            public bool DoesPathIntersectZone(Float2 a, Float2 b, float inflationRadius) => Shape.DoesPathIntersectZone(a, b, inflationRadius);
            public Float2? GetFirstSegmentEntry(Float2 a, Float2 b) => Shape.GetFirstSegmentEntry(a, b);
            public Float2? GetLastSegmentExit(Float2 a, Float2 b) => Shape.GetLastSegmentExit(a, b);
            public ESightLineEffect EvaluateSightLine(Position attacker, Position target)
                => _closed ? ESightLineEffect.Blocking : ESightLineEffect.Clear;
        }
    }
}
