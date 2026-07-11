using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A5-9 (Chris's option 2) - matchup-aware deployment: lanes are scored by unit-vs-unit
    // fit against the enemies already on the table, and matchup-SENSITIVE units are held for
    // late deploy picks so the counters place with more of the enemy layout visible.
    [TestFixture]
    public class TacticianDeploymentMatchupTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private PlayerID _us;
        private PlayerID _them;
        private Dictionary<PlayerID, List<DataBinding<UnitData>>> _units = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _units = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
        }

        [Test]
        public async Task AntiTankPlatform_DeploysOppositeTheTank_NotTheHorde()
        {
            // Two lanes: their Tough tank sits over the x=12 objective, their melee horde over
            // x=52. The melter platform kills tanks and dies to hordes - it must pick the tank lane.
            _store.Create(new ObjectiveData(new Position(12f, 24f), _store));
            _store.Create(new ObjectiveData(new Position(52f, 24f), _store));
            MakeUnit(_them, "Tank", 1, new Weapon("Cannon", 24f, 3, 2), atX: 12f, atZ: 40f,
                defense: 2, woundsPerModel: 6);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 52f, atZ: 40f);
            var melters = MakeUnit(_us, "Melters", 3, new Weapon("Melter", 24f, 2, 4), atX: 0f, atZ: 0f);
            BuildArmies();
            var resolver = new TacticianPlaceObjectsResolver<ModelData>(_tableState, _evaluator);

            var reply = await resolver.Resolve(new PlaceObjectsRequest<ModelData>(
                _us, TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName,
                new RectangularZone(0f, 64f, 0f, 10f), melters.GetValue().ModelBindings));

            var placed = ((Selected<List<PlacedObjectEntry<ModelData>>>)reply).Value;
            float cx = placed.Average(p => p.Position.x);
            Assert.That(Math.Abs(cx - 12f), Is.LessThanOrEqualTo(10f),
                "the anti-tank platform deploys into the tank's lane");
            Assert.That(Math.Abs(cx - 52f), Is.GreaterThan(20f),
                "and stays away from the horde it cannot fight");
        }

        [Test]
        public async Task Generalist_DeploysBeforeTheCounter()
        {
            // Blade chaff is equally mediocre into everything; the melter platform's
            // value swings hard by target. Hold the counter back: chaff deploys first.
            MakeUnit(_them, "Tank", 1, new Weapon("Cannon", 24f, 3, 2), atX: 12f, atZ: 40f,
                defense: 2, woundsPerModel: 6);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 52f, atZ: 40f);
            var melters = MakeUnit(_us, "Melters", 3, new Weapon("Melter", 24f, 2, 4), atX: 0f, atZ: 0f);
            var chaff = MakeUnit(_us, "Chaff", 5, new Weapon("Blade", 0f, 1, 0), atX: 2f, atZ: 0f);
            BuildArmies();
            var resolver = new TacticianUnitSelectionResolver(
                new TacticianPlanner(_tableState, _evaluator),
                new FDG.Ai.Resolvers.AiSelectionResolver<UnitData>(), _tableState, _evaluator);

            DataBinding<UnitData> pick = await resolver.Resolve(new SelectionRequest<UnitData>(
                _us, TacticianUnitSelectionResolver.DeployOrderInstructions,
                new List<SelectionRequest<UnitData>.ValidOption>
                {
                    new(melters, "Melters"), new(chaff, "Chaff"),
                },
                new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: false));

            Assert.That(pick.Reference, Is.EqualTo(chaff.Reference),
                "the generalist deploys early; the counter waits for the enemy layout");
        }

        // --- fixtures ---

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int modelCount,
            Weapon weapon, float atX, float atZ, int quality = 4, int defense = 4, int woundsPerModel = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality, defense, modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (!_units.TryGetValue(owner, out List<DataBinding<UnitData>>? list))
                _units[owner] = list = new List<DataBinding<UnitData>>();
            list.Add(binding);
            return binding;
        }

        private void BuildArmies()
        {
            foreach ((PlayerID owner, List<DataBinding<UnitData>> list) in _units)
                _store.Create(new ArmyData(owner, list));
        }
    }
}
