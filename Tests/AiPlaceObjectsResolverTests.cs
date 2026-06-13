using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Proves the faithful Ambush placement constraint: when a request sets MinDistanceFromEnemiesInches,
    // AiPlaceObjectsResolver returns positions strictly farther than that from every enemy model. The
    // Ambush integration test uses a canned requester, so the enforcement is verified here directly.
    [TestFixture]
    public class AiPlaceObjectsResolverTests
    {
        [Test]
        public async Task PlacesAllModelsOverMinDistanceFromEnemies()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();

            var selfPlayer = new PlayerID(System.Guid.NewGuid());
            var enemyPlayer = new PlayerID(System.Guid.NewGuid());

            // Enemy model parked mid-table.
            var enemyPos = new Position(36f, 24f);
            var enemyModel = new ModelData(0.5f, new List<Weapon>(), enemyPos, store);
            var enemyBinding = store.GetDataBinding<ModelData>(store.Create(enemyModel));
            var enemyUnit = new UnitData(enemyPlayer, "Enemies", 4, 4, new List<DataBinding<ModelData>> { enemyBinding });
            store.Create(enemyUnit);

            // Placing unit (2 models, still at origin / in reserve).
            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var placingUnit = new UnitData(selfPlayer, "Infiltrators", 4, 4, placing);
            store.Create(placingUnit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);

            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            var zoneBinding = store.GetDataBinding<RectangularZone>(store.Create(wholeTable));

            const float minDist = 9f;
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Ambush Deploy", zoneBinding,
                placing, minDistanceFromEnemiesInches: minDist);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(2));
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float dx = entry.Position.x - enemyPos.x;
                float dz = entry.Position.z - enemyPos.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                Assert.That(dist, Is.GreaterThanOrEqualTo(minDist),
                    $"AI deep-strike placement must stay {minDist}\" from enemies (was {dist:F1}\").");
            }
        }

        [Test]
        public async Task DoesNotPlaceModelsOnImpassibleTerrain()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var selfPlayer = new PlayerID(System.Guid.NewGuid());

            // A deployment strip with an impassible wall splitting it at x=8..12 (full height). #048.
            var zone = new RectangularZone(0f, 20f, 0f, 12f);
            var zoneBinding = store.GetDataBinding<RectangularZone>(store.Create(zone));
            var wall = new TerrainData(ETerrainType.Impassible, new RectangularZone(8f, 12f, 0f, 12f));
            store.Create(wall);

            // Five models to deploy.
            var placing = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 5; i++)
            {
                var m = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f), store);
                placing.Add(store.GetDataBinding<ModelData>(store.Create(m)));
            }
            var unit = new UnitData(selfPlayer, "Warriors", 4, 4, placing);
            store.Create(unit);

            var tableState = new TableState(store);
            var resolver = new AiPlaceObjectsResolver<ModelData>(tableState);
            var request = new PlaceObjectsRequest<ModelData>(selfPlayer, "Place Unit Models", zoneBinding, placing);

            List<PlacedObjectEntry<ModelData>> result = await resolver.Resolve(request);

            Assert.That(result, Has.Count.EqualTo(5));
            var impassible = new List<ITerrain> { wall };
            foreach (PlacedObjectEntry<ModelData> entry in result)
            {
                float r = entry.Binding.GetValue().BaseRadiusInches;
                Assert.That(PlacementUtilities.OverlapsImpassibleTerrain(entry.Position, r, impassible), Is.False,
                    $"AI must not deploy a model overlapping impassible terrain (placed at {entry.Position.x:F1}, {entry.Position.z:F1}).");
            }
        }
    }
}
