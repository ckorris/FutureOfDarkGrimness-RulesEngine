using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029: the AI answers the Aircraft forced move with the shortest distance in the band that stays on the
    // table (staying in play beats flying far), and confirms the mandatory fly-off when nothing stays on.
    [TestFixture]
    public class AiAircraftAdvanceResolverTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task PicksTheShortestOnTableDistance()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 5)); // facing +z: 30" → (10, 35), on-table
            var request = new AircraftAdvanceRequest(new PlayerID(Guid.NewGuid()), "test", unit,
                new Float2(0f, 1f), 30f, 36f);

            AircraftAdvanceResult result = await new AiAircraftAdvanceResolver().Resolve(request);

            Assert.That(result.FliesOffTable, Is.False);
            Assert.That(result.DistanceInches, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public async Task ConfirmsTheMandatoryFlyOff_WhenNoDistanceStaysOn()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 40)); // facing +z: even 30" is past the top edge
            var request = new AircraftAdvanceRequest(new PlayerID(Guid.NewGuid()), "test", unit,
                new Float2(0f, 1f), 30f, 36f);

            AircraftAdvanceResult result = await new AiAircraftAdvanceResolver().Resolve(request);

            Assert.That(result.FliesOffTable, Is.True);
        }

        private DataBinding<UnitData> MakeUnit(Position pos)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), pos, _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Jet", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { binding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
