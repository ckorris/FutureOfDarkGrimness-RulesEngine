using FDG.Ai.Gunline;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 tooling - the Gunline profile is a scripted human stand-in (hold the line, shoot,
    // only move to claim enemy-free objectives). These pin the script's three branches, plus
    // the Tactician's decision-log sink that the same tooling pass added.
    [TestFixture]
    public class GunlineProfileTests
    {
        private static readonly List<string> AllActions = new() { "Move", "Charge", "Shoot", "Pass" };

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
        public void EnemyInRange_StandsAndShoots()
        {
            var line = MakeUnit(_us, "Line", 5, new Weapon("Rifle", 24f, 1, 0), atX: 20f, atZ: 10f);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 20f, atZ: 28f);
            BuildArmies();
            var planner = new GunlinePlanner(_tableState, _evaluator);

            planner.BeginActivation(line);

            Assert.That(planner.ChooseAction(AllActions), Is.EqualTo("Shoot"));
            Assert.That(planner.TakePlannedMove(line), Is.Null, "shooting happens in place");
        }

        [Test]
        public void NothingInRange_ClaimsTheSafeObjective()
        {
            // Enemy is out of rifle range and far from the marker; the marker is free - claim it.
            _store.Create(new ObjectiveData(new Position(30f, 14f), _store));
            var line = MakeUnit(_us, "Line", 5, new Weapon("Rifle", 24f, 1, 0), atX: 20f, atZ: 10f);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 60f, atZ: 44f);
            BuildArmies();
            var planner = new GunlinePlanner(_tableState, _evaluator);

            planner.BeginActivation(line);
            string? action = planner.ChooseAction(AllActions);
            var move = planner.TakePlannedMove(line);

            Assert.That(action, Is.EqualTo("Move"));
            Assert.That(move, Is.Not.Null, "the claim is a real cached move");
        }

        [Test]
        public void NothingInRange_ObjectiveGuarded_HoldsTheLine()
        {
            // The only marker has the horde parked on it: not safe, so the script holds (Pass)
            // rather than walking the line into the horde's reach.
            _store.Create(new ObjectiveData(new Position(58f, 40f), _store));
            var line = MakeUnit(_us, "Line", 5, new Weapon("Rifle", 24f, 1, 0), atX: 20f, atZ: 10f);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 58f, atZ: 42f);
            BuildArmies();
            var planner = new GunlinePlanner(_tableState, _evaluator);

            planner.BeginActivation(line);

            Assert.That(planner.ChooseAction(AllActions), Is.EqualTo("Pass"));
        }

        [Test]
        public void TacticianDecisionLog_NarratesTheChoice()
        {
            var lines = new List<string>();
            var shooters = MakeUnit(_us, "Shooters", 5, new Weapon("Rifle", 24f, 1, 0), atX: 20f, atZ: 10f);
            MakeUnit(_them, "Horde", 10, new Weapon("Blade", 0f, 2, 0), atX: 20f, atZ: 28f);
            BuildArmies();
            var planner = new TacticianPlanner(_tableState, _evaluator, lines.Add);

            planner.BeginActivation(shooters);
            string? action = planner.ChooseAction(AllActions);

            Assert.That(action, Is.Not.Null);
            Assert.That(lines, Is.Not.Empty, "the sink saw the decision");
            Assert.That(lines[0], Does.StartWith("plan Shooters"));
            Assert.That(lines.Count, Is.GreaterThan(1), "the full candidate table follows the winner");
        }

        // --- fixtures (mirrors TacticianDeploymentMatchupTests) ---

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int modelCount,
            Weapon weapon, float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, 4, 4, modelBindings);
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
