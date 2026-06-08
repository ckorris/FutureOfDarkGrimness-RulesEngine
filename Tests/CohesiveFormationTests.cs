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

        private List<DataBinding<ModelData>> MakeModels(params Position[] positions)
        {
            var list = new List<DataBinding<ModelData>>(positions.Length);
            foreach (var pos in positions)
            {
                var md = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    specialRules: new List<SpecialRule>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                list.Add(_store.GetDataBinding<ModelData>(_store.Create(md)));
            }
            return list;
        }
    }
}
