using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Regression coverage for the casualty-cohesion crash: when a unit loses a model, the survivors can
    // end up >1" apart (a hole), and a rigid translate of that formation is rejected for breaking
    // cohesion — crashing DefinePathStage. CohesiveFormation.PackGrid re-forms the living models into a
    // grid that always satisfies the 1" rule, so the AI and CLI movement resolvers can still produce a
    // legal move. Validated against the real MovementUtilities.ValidatePaths cohesion check.
    [TestFixture]
    public class CohesiveFormationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void PackGrid_ScatteredSurvivors_ProduceCohesionValidFormation()
        {
            // Four models scattered far beyond the 1" cohesion limit (as if a casualty split the unit).
            var models = MakeModels(new Position(0, 0), new Position(6, 0), new Position(0, 6), new Position(6, 6));

            var entries = CohesiveFormation.PackGrid(models, centerX: 12f, centerZ: 12f);

            bool valid = MovementUtilities.ValidatePaths(entries, maxDistanceInches: 100f, out var errors);
            Assert.That(valid, Is.True,
                "re-packed formation must satisfy cohesion. Errors: " +
                string.Join(", ", errors.Select(e => MovementUtilities.ErrorReasonToString(e.ErrorReasonType))));
        }

        [Test]
        public void PackGrid_CentersFormationOnTarget()
        {
            var models = MakeModels(new Position(0, 0), new Position(6, 0), new Position(0, 6));

            var entries = CohesiveFormation.PackGrid(models, centerX: 20f, centerZ: 15f);

            float avgX = entries.Average(e => e.Positions[^1].x);
            float avgZ = entries.Average(e => e.Positions[^1].z);
            Assert.That(avgX, Is.EqualTo(20f).Within(0.5f), "formation centres on the target X.");
            Assert.That(avgZ, Is.EqualTo(15f).Within(0.5f), "formation centres on the target Z.");
        }

        [Test]
        public void PackGrid_SingleModel_MovesToTarget()
        {
            var models = MakeModels(new Position(0, 0));

            var entries = CohesiveFormation.PackGrid(models, centerX: 7f, centerZ: 3f);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].Positions[^1].x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(entries[0].Positions[^1].z, Is.EqualTo(3f).Within(0.001f));
        }

        // #150: a wide rectangular base (4"×1.5") can't be packed cohesively by a single spacing scalar — the
        // circumscribing radius over-spaces the 1.5" short axis (rows 2.87" apart → breaks the 1" rule) and the
        // inscribed radius under-spaces the 4" long axis (bases overlap). Per-axis grid spacing packs it tight
        // on both. Validated against the real shape-aware cohesion check (this crashed the game before the fix).
        [Test]
        public void PackGrid_WideRectangles_ProduceCohesionValidFormation()
        {
            var models = MakeRectModels(4f, 1.5f,
                new Position(0, 0), new Position(30, 0), new Position(0, 30), new Position(30, 30),
                new Position(15, 15));

            var entries = CohesiveFormation.PackGrid(models, centerX: 20f, centerZ: 20f);

            bool valid = MovementUtilities.ValidatePaths(entries, maxDistanceInches: 200f, out var errors);
            Assert.That(valid, Is.True,
                "re-packed wide-rectangle formation must satisfy cohesion. Errors: " +
                string.Join(", ", errors.Select(e => MovementUtilities.ErrorReasonToString(e.ErrorReasonType))));
        }

        // #159: a unit mixing a large base (a joined hero / monster, r=1.5") with small troopers (r=0.5")
        // could not be re-packed cohesively — GridSpacingXZ spaces the WHOLE grid by the largest base, so a
        // small model lands >1" (base-to-base) from every neighbour and fails the 1" nearest-neighbour rule.
        // The AI/auto movement resolver falls back on this pack, so DefinePathStage rejected the result and
        // crashed the game (the intermittent HEF-army cohesion crash). Validated against the real cohesion check.
        [Test]
        public void PackGrid_MixedBaseSizes_ProduceCohesionValidFormation()
        {
            var models = new List<DataBinding<ModelData>>
            {
                MakeCircle(1.5f, new Position(0, 0)),
                MakeCircle(0.5f, new Position(6, 0)),
                MakeCircle(0.5f, new Position(0, 6)),
                MakeCircle(0.5f, new Position(6, 6)),
                MakeCircle(0.5f, new Position(3, 3)),
            };

            var entries = CohesiveFormation.PackGrid(models, centerX: 20f, centerZ: 20f);

            bool valid = MovementUtilities.ValidatePaths(entries, maxDistanceInches: 200f, out var errors);
            Assert.That(valid, Is.True,
                "re-packed mixed-base formation must satisfy cohesion. Errors: " +
                string.Join(", ", errors.Select(e => MovementUtilities.ErrorReasonToString(e.ErrorReasonType))));
        }

        // #159 helpers used by the consolidation re-form.
        [Test]
        public void IsCohesive_DetectsCasualtyHole()
        {
            // Two survivors 2.2" apart (a mid-unit casualty hole) are out of coherency; 0.6" apart are in.
            var holed = MakeModels(new Position(0, 0), new Position(0, 3.2f)); // base-to-base 2.2" (> 1")
            Assert.That(CohesiveFormation.IsCohesive(holed), Is.False);

            var tight = MakeModels(new Position(0, 0), new Position(0, 1.1f)); // base-to-base 0.1" (<= 1")
            Assert.That(CohesiveFormation.IsCohesive(tight), Is.True);
        }

        [Test]
        public void ReformTowardWithinCap_HoledUnit_TightensButRespectsCap()
        {
            // Survivors 2.2" apart; a 1" cap can't fully close the gap but must pull them closer and move no
            // model more than the cap.
            var models = MakeModels(new Position(0, 0), new Position(0, 3.2f));
            float startGap = 2.2f; // base-to-base (radius 0.75 each, centres 3.2 apart)

            var entries = CohesiveFormation.ReformTowardWithinCap(models, centerX: 0f, centerZ: 1.6f, maxStepInches: 1f);

            foreach (var e in entries)
            {
                float moved = Position.GetDistance2D(e.Model.GetValue().Position, e.Positions[^1]);
                Assert.That(moved, Is.LessThanOrEqualTo(1f + 0.0001f), "no model moves more than the cap");
            }
            float endCentreGap = Position.GetDistance2D(entries[0].Positions[^1], entries[1].Positions[^1]);
            float endBaseGap = endCentreGap - 0.75f - 0.75f;
            Assert.That(endBaseGap, Is.LessThan(startGap), "the survivors end closer than they started");
        }

        private DataBinding<ModelData> MakeCircle(float radius, Position pos)
        {
            var md = new ModelData(radius, new List<Weapon>(), pos, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(md));
        }

        private List<DataBinding<ModelData>> MakeRectModels(float w, float h, params Position[] positions)
        {
            var list = new List<DataBinding<ModelData>>(positions.Length);
            foreach (var pos in positions)
            {
                var md = new ModelData(new RectangleBase(w, h), new List<Weapon>(), pos, _store);
                list.Add(_store.GetDataBinding<ModelData>(_store.Create(md)));
            }
            return list;
        }

        private List<DataBinding<ModelData>> MakeModels(params Position[] positions)
        {
            var list = new List<DataBinding<ModelData>>(positions.Length);
            foreach (var pos in positions)
            {
                var md = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                list.Add(_store.GetDataBinding<ModelData>(_store.Create(md)));
            }
            return list;
        }
    }
}
