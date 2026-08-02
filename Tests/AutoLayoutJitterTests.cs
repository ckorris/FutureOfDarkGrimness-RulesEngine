using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Seeded midfield jitter for AutoFromLayout terrain
    /// (<see cref="PlaceTerrainStage.TryJitterMidfieldPiece"/>).
    /// </summary>
    [TestFixture]
    public class AutoLayoutJitterTests
    {
        private const float TableW = 72f;
        private const float TableH = 48f;
        private const float Deploy = 12f;

        private static readonly IZone MidfieldRect = new RectangularZone(33, 39, 22, 26);

        [Test]
        public void SameSeed_SamePose()
        {
            IZone? a = PlaceTerrainStage.TryJitterMidfieldPiece(
                MidfieldRect, new Random(42), TableW, TableH, Deploy, Array.Empty<ITerrain>());
            IZone? b = PlaceTerrainStage.TryJitterMidfieldPiece(
                MidfieldRect, new Random(42), TableW, TableH, Deploy, Array.Empty<ITerrain>());

            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Float2 ca = a!.GetAABBCenter();
            Float2 cb = b!.GetAABBCenter();
            Assert.That(ca.X, Is.EqualTo(cb.X));
            Assert.That(ca.Y, Is.EqualTo(cb.Y));
        }

        [Test]
        public void DifferentSeeds_UsuallyDifferentPoses()
        {
            Float2 first = PlaceTerrainStage.TryJitterMidfieldPiece(
                MidfieldRect, new Random(1), TableW, TableH, Deploy, Array.Empty<ITerrain>())!.GetAABBCenter();
            Float2 second = PlaceTerrainStage.TryJitterMidfieldPiece(
                MidfieldRect, new Random(2), TableW, TableH, Deploy, Array.Empty<ITerrain>())!.GetAABBCenter();

            Assert.That(first.X != second.X || first.Y != second.Y,
                "Two different seeds landed on the identical pose - jitter is not drawing from the RNG.");
        }

        [Test]
        public void Pose_StaysWithinJitterRadius_OnTable_AndOutOfDeploymentBands()
        {
            Float2 origin = MidfieldRect.GetAABBCenter();
            for (int seed = 0; seed < 50; seed++)
            {
                IZone? pose = PlaceTerrainStage.TryJitterMidfieldPiece(
                    MidfieldRect, new Random(seed), TableW, TableH, Deploy, Array.Empty<ITerrain>());
                Assert.That(pose, Is.Not.Null, $"Seed {seed}: no pose on an empty table.");

                Float2 c = pose!.GetAABBCenter();
                Assert.That(Math.Abs(c.X - origin.X),
                    Is.LessThanOrEqualTo(PlaceTerrainStage.MidfieldJitterMaxInches + 0.001f));
                Assert.That(Math.Abs(c.Y - origin.Y),
                    Is.LessThanOrEqualTo(PlaceTerrainStage.MidfieldJitterMaxInches + 0.001f));
                Assert.That(c.Y, Is.GreaterThanOrEqualTo(Deploy).And.LessThanOrEqualTo(TableH - Deploy),
                    $"Seed {seed}: piece center drifted into a deployment band.");
                Assert.That(TerrainPlacementValidator.Check(pose, TableW, TableH, Array.Empty<ITerrain>()),
                    Is.EqualTo(TerrainPlacementValidity.Valid), $"Seed {seed}: pose is off-table.");
            }
        }

        [Test]
        public void CrowdedNeighborhood_ResolvesToNonOverlappingPose()
        {
            // Ring the authored spot with terrain so most jittered candidates collide; the
            // shrinking-jitter fallback must still land a valid pose near the authored one.
            var placed = new ITerrain[]
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(28, 32, 20, 28)),
                new TerrainData(ETerrainType.Cover, new RectangularZone(40, 44, 20, 28)),
                new TerrainData(ETerrainType.Cover, new RectangularZone(28, 44, 27.5f, 30)),
                new TerrainData(ETerrainType.Cover, new RectangularZone(28, 44, 18, 20.5f)),
            };

            for (int seed = 0; seed < 20; seed++)
            {
                IZone? pose = PlaceTerrainStage.TryJitterMidfieldPiece(
                    MidfieldRect, new Random(seed), TableW, TableH, Deploy, placed);
                Assert.That(pose, Is.Not.Null, $"Seed {seed}: authored pose is clear, so a pose must resolve.");
                Assert.That(TerrainPlacementValidator.Check(pose!, TableW, TableH, placed),
                    Is.EqualTo(TerrainPlacementValidity.Valid), $"Seed {seed}: resolved pose overlaps.");
            }
        }

        [Test]
        public void AuthoredPoseBlocked_ReturnsNull_InsteadOfOverlapping()
        {
            // A giant slab covers the entire midfield: no candidate anywhere can be valid.
            var placed = new ITerrain[]
            {
                new TerrainData(ETerrainType.Cover, new RectangularZone(0.1f, 71.9f, Deploy, TableH - Deploy)),
            };

            IZone? pose = PlaceTerrainStage.TryJitterMidfieldPiece(
                MidfieldRect, new Random(7), TableW, TableH, Deploy, placed);

            Assert.That(pose, Is.Null);
        }

        [Test]
        public void CircularPiece_JittersWithoutRotationWrapper()
        {
            IZone? pose = PlaceTerrainStage.TryJitterMidfieldPiece(
                new CircularZone(new Float2(36, 24), 5), new Random(3), TableW, TableH, Deploy,
                Array.Empty<ITerrain>());

            Assert.That(pose, Is.Not.Null);
            Assert.That(pose, Is.TypeOf<CircularZone>());
        }
    }
}
