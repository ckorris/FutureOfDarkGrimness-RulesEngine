using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #277: FormationLibrary is the single home for row-formation geometry — partition generation
    // (line, 5x2, 4-3-3, ...), per-model row layout, and the 9" legality filter. PackGrid and the GUI
    // group placement/movement resolvers all lay rows out through it, so these tests pin the shared
    // conventions: no lone-model rows (#159), per-model spacing keeps any base mix inside the 1" rule,
    // and shapes that would break the 9" all-pairs span are never offered.
    [TestFixture]
    public class FormationLibraryTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void RowPartitions_TenModels_YieldsExpectedShapes()
        {
            var partitions = FormationLibrary.RowPartitions(10);

            var expected = new[]
            {
                new[] { 10 },
                new[] { 5, 5 },
                new[] { 4, 3, 3 },
                new[] { 3, 3, 2, 2 },
                new[] { 2, 2, 2, 2, 2 },
            };
            Assert.That(partitions.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(partitions[i], Is.EqualTo(expected[i]), $"partition {i}");
        }

        [Test]
        public void RowPartitions_NeverEmitALoneModelRow()
        {
            // A row of 1 has no in-row neighbour for the 1" rule, which mixed base sizes can't always
            // bridge cross-row (#159) — so no partition may contain a row smaller than 2 (except n=1).
            for (int n = 2; n <= 30; n++)
                foreach (var partition in FormationLibrary.RowPartitions(n))
                {
                    Assert.That(partition.Sum(), Is.EqualTo(n), $"partition of {n} sums to {n}");
                    Assert.That(partition.Min(), Is.GreaterThanOrEqualTo(2), $"no lone-model row for n={n}");
                }

            var single = FormationLibrary.RowPartitions(1);
            Assert.That(single.Count, Is.EqualTo(1));
            Assert.That(single[0], Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void Describe_NamesLineEqualRowsAndMixedRows()
        {
            Assert.That(FormationLibrary.Describe(new[] { 10 }), Is.EqualTo("line (10)"));
            Assert.That(FormationLibrary.Describe(new[] { 5, 5 }), Is.EqualTo("5x2"));
            Assert.That(FormationLibrary.Describe(new[] { 4, 3, 3 }), Is.EqualTo("4-3-3"));
        }

        [Test]
        public void LegalFormations_ExcludesShapesBreakingTheNineInchSpan()
        {
            // 10 small bases (r=0.5): the line spans 8.9" base-to-base — offered. 10 large bases
            // (r=0.75): the line spans 12.9" — filtered out, while 5x2 stays.
            var (hxS, hzS, rS) = UniformExtents(10, 0.5f);
            var small = FormationLibrary.LegalFormations(hxS, hzS, rS, gap: 0.1f,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
            Assert.That(small.Any(f => f.RowCounts.Length == 1), Is.True, "small-base line is legal");

            var (hxL, hzL, rL) = UniformExtents(10, 0.75f);
            var large = FormationLibrary.LegalFormations(hxL, hzL, rL, gap: 0.1f,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
            Assert.That(large.Any(f => f.RowCounts.Length == 1), Is.False, "large-base line is filtered");
            Assert.That(large.Any(f => f.Name == "5x2"), Is.True, "5x2 remains legal");
        }

        // Every offered formation, laid out for a MIXED-base unit via the slot-assigning planner, must
        // pass the real engine cohesion validation — the same gate the committed move faces.
        [Test]
        public void PlanFormationOffsets_MixedBases_EveryLegalFormationIsCohesionValid()
        {
            var models = new List<DataBinding<ModelData>>
            {
                MakeCircle(1.5f, new Position(10, 10)),
                MakeCircle(0.5f, new Position(16, 10)),
                MakeCircle(0.5f, new Position(10, 16)),
                MakeCircle(0.5f, new Position(16, 16)),
                MakeCircle(0.5f, new Position(13, 13)),
                MakeCircle(0.5f, new Position(13, 10)),
            };
            var current = models.Select(m => m.GetValue().Position).ToList();
            var halfXs = models.Select(m => m.GetValue().BaseShape.CircumscribedRadiusInches).ToList();
            var radii = halfXs;

            var legal = FormationLibrary.LegalFormations(halfXs, halfXs, radii, gap: 0.1f,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
            Assert.That(legal.Count, Is.GreaterThan(0));

            foreach (var formation in legal)
            {
                var offsets = FormationLibrary.PlanFormationOffsets(current, halfXs, halfXs, formation.RowCounts, gap: 0.1f);
                var entries = new List<ModelMoveEntry>(models.Count);
                for (int i = 0; i < models.Count; i++)
                    entries.Add(new ModelMoveEntry(models[i],
                        new List<Position> { new Position(20f + offsets[i].dx, 20f + offsets[i].dz) }));

                bool valid = MovementUtilities.ValidatePaths(entries, maxDistanceInches: 200f, out var errors);
                Assert.That(valid, Is.True,
                    $"formation {formation.Name} must satisfy cohesion. Errors: " +
                    string.Join(", ", errors.Select(e => MovementUtilities.ErrorReasonToString(e.ErrorReasonType))));
            }
        }

        [Test]
        public void PlanFormationOffsets_AssignsEveryModelOneSlotCentredOnTheCentroid()
        {
            var current = new List<Position>
            {
                new Position(0, 0), new Position(2, 0), new Position(4, 0),
                new Position(0, 2), new Position(2, 2), new Position(4, 2),
            };
            var (hx, hz, _) = UniformExtents(6, 0.5f);

            var offsets = FormationLibrary.PlanFormationOffsets(current, hx, hz, new[] { 3, 3 }, gap: 0.1f);

            Assert.That(offsets.Length, Is.EqualTo(6));
            Assert.That(offsets.Average(o => o.dx), Is.EqualTo(0f).Within(0.001f));
            Assert.That(offsets.Average(o => o.dz), Is.EqualTo(0f).Within(0.001f));
            // No two models share a slot.
            for (int i = 0; i < offsets.Length; i++)
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    float dx = offsets[i].dx - offsets[j].dx, dz = offsets[i].dz - offsets[j].dz;
                    Assert.That(MathF.Sqrt(dx * dx + dz * dz), Is.GreaterThan(0.5f), $"models {i}/{j} share a slot");
                }
        }

        private static (List<float> hx, List<float> hz, List<float> r) UniformExtents(int n, float radius)
        {
            var list = Enumerable.Repeat(radius, n).ToList();
            return (list, list, list);
        }

        private DataBinding<ModelData> MakeCircle(float radius, Position pos)
        {
            var md = new ModelData(radius, new List<Weapon>(), pos, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(md));
        }
    }
}
