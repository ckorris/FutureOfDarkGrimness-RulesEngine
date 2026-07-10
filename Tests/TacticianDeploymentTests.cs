using System;
using FDG.Ai.Resolvers;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A4b — objective-aware deployment: units spread across objectives (nearest first),
    // melee crowds the forward edge, shooters hang back; every non-deployment placement is
    // bit-identical to the solo resolver (the subclass only re-aims the preferred centre).
    [TestFixture]
    public class TacticianDeploymentTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task MeleeUnit_DeploysOnTheForwardEdge_UnderTheNearestObjective()
        {
            // Zone at the bottom of the table (forward edge = Top); one objective at x=30.
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var zone = BottomZone();
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState);

            var placed = await Place(resolver, zone, MakeModels(4, Blade()));

            (Position centroid, _) = Centroid(placed);
            Assert.That(centroid.x, Is.EqualTo(30f).Within(2.5f), "aimed at the objective's lane");
            Assert.That(centroid.z, Is.GreaterThanOrEqualTo(9f), "melee crowds the forward edge");
        }

        [Test]
        public async Task Shooter_DeploysDeeperThanMelee_OnTheSameLane()
        {
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var zone = BottomZone();
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState);

            var meleePlaced = await Place(resolver, zone, MakeModels(4, Blade()));
            var shooterPlaced = await Place(resolver, zone, MakeModels(4, Rifle()));

            (Position meleeC, _) = Centroid(meleePlaced);
            (Position shooterC, _) = Centroid(shooterPlaced);
            Assert.That(shooterC.z, Is.LessThan(meleeC.z - 1f),
                "long-range unit stands back from the forward edge");
        }

        [Test]
        public async Task SuccessiveUnits_SpreadAcrossObjectives()
        {
            _store.Create(new ObjectiveData(new Position(12f, 24f), _store));
            _store.Create(new ObjectiveData(new Position(36f, 24f), _store));
            var zone = BottomZone();
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState);

            var first = await Place(resolver, zone, MakeModels(4, Blade()));
            var second = await Place(resolver, zone, MakeModels(4, Blade()));

            (Position c1, _) = Centroid(first);
            (Position c2, _) = Centroid(second);
            Assert.That(MathF.Abs(c1.x - c2.x), Is.GreaterThanOrEqualTo(12f),
                "two units, two objectives - not one dogpile");
        }

        [Test]
        public async Task NonDeploymentPlacement_IsBitIdenticalToSolo()
        {
            // Same store state, same request twice: the subclass must defer to base for any
            // TaskName that is not the deployment literal (disembark here).
            _store.Create(new ObjectiveData(new Position(30f, 24f), _store));
            var zone = BottomZone();
            var solo = new AiPlaceObjectsResolver<ModelData>(_tableState);
            var tactician = new TacticianPlaceObjectsResolver<ModelData>(_tableState);

            var models = MakeModels(3, Rifle());
            var soloReply = await solo.Resolve(new PlaceObjectsRequest<ModelData>(
                _player, "Disembark Grunts", zone, models));
            var tacticianReply = await tactician.Resolve(new PlaceObjectsRequest<ModelData>(
                _player, "Disembark Grunts", zone, models));

            var soloPlaced = ((Selected<List<PlacedObjectEntry<ModelData>>>)soloReply).Value;
            var tacticianPlaced = ((Selected<List<PlacedObjectEntry<ModelData>>>)tacticianReply).Value;
            for (int i = 0; i < soloPlaced.Count; i++)
            {
                Assert.That(tacticianPlaced[i].Position.x, Is.EqualTo(soloPlaced[i].Position.x));
                Assert.That(tacticianPlaced[i].Position.z, Is.EqualTo(soloPlaced[i].Position.z));
            }
        }

        // --- fixtures ---

        private static RectangularZone BottomZone() =>
            new RectangularZone(left: 0f, right: 48f, bottom: 0f, top: 12f);

        private async Task<List<PlacedObjectEntry<ModelData>>> Place(
            TacticianPlaceObjectsResolver<ModelData> resolver, RectangularZone zone,
            List<DataBinding<ModelData>> models)
        {
            var reply = await resolver.Resolve(new PlaceObjectsRequest<ModelData>(
                _player, TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName, zone, models));
            return ((Selected<List<PlacedObjectEntry<ModelData>>>)reply).Value;
        }

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Blade() => new Weapon("Blade", 0f, 2, 0);

        private List<DataBinding<ModelData>> MakeModels(int count, Weapon weapon)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < count; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, new Position(0f, 0f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_player, "U", quality: 4, defense: 4, modelBindings: models);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return models;
        }

        private static (Position, int) Centroid(List<PlacedObjectEntry<ModelData>> placed)
        {
            float x = placed.Average(p => p.Position.x);
            float z = placed.Average(p => p.Position.z);
            return (new Position(x, z), placed.Count);
        }
    }
}
