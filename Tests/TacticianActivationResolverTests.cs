using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A4-1 — activation order by urgency (the A0 identity pin's behavioral successor):
    // the Tactician activates the unit with the most to gain, flip, or lose, instead of the
    // solo bot's first-in-list.
    [TestFixture]
    public class TacticianActivationResolverTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private TacticianActivationResolver _resolver = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _resolver = new TacticianActivationResolver(_tableState,
                new RuleEvaluator(new ProbabilisticDiceRoller()));
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task PicksTheUnitWithAKillOpportunity_OverAnIdleOne()
        {
            // Both ours; only the near one can hurt the enemy this activation (24" rifle + 6" advance
            // vs an enemy 12" away; the idle one sits 60" away across the table).
            var shooter = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 24f);
            var idler = MakeUnit(_us, 3, Rifle(), atX: 2f, atZ: 46f);
            MakeUnit(_them, 3, Rifle(), atX: 32f, atZ: 24f);

            DataBinding<UnitData> chosen = await _resolver.Resolve(Request(idler, shooter));

            Assert.That(chosen, Is.EqualTo(shooter), "the unit that can actually hurt something acts first");
        }

        [Test]
        public async Task PicksTheUnitThatCanFlipAnObjective_OverAPureShootout()
        {
            _store.Create(new ObjectiveData(new Position(10f, 40f), _store));
            var flipper = MakeUnit(_us, 3, Rifle(), atX: 10f, atZ: 30f);  // 10" from objective, rush 12
            var brawler = MakeUnit(_us, 3, Rifle(), atX: 20f, atZ: 10f);  // shooting range of the enemy
            MakeUnit(_them, 3, Rifle(), atX: 30f, atZ: 10f);

            DataBinding<UnitData> chosen = await _resolver.Resolve(Request(brawler, flipper));

            Assert.That(chosen, Is.EqualTo(flipper), "objective flips outrank exchanges of fire");
        }

        [Test]
        public async Task NoOpportunitiesAnywhere_StillResolvesToSomeValidOption()
        {
            var a = MakeUnit(_us, 3, Rifle(), atX: 5f, atZ: 5f);
            var b = MakeUnit(_us, 3, Rifle(), atX: 10f, atZ: 5f);
            // No enemies, no objectives: all scores zero - any valid option is acceptable, never a throw.

            DataBinding<UnitData> chosen = await _resolver.Resolve(Request(a, b));

            Assert.That(chosen == a || chosen == b, Is.True);
        }

        [Test]
        public async Task LoadedTransport_ActsBeforeItsEmbarkedCargo()
        {
            // A5-6 (Chris): boat-then-payload - the transport must move before the cargo's own
            // activation decides whether to get out.
            var transport = MakeUnit(_us, 1, Rifle(), atX: 20f, atZ: 24f);
            var cargo = MakeUnit(_us, 4, Rifle(), atX: 20f, atZ: 24f);
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
            // No enemies/objectives: only the transport biases separate them.

            DataBinding<UnitData> chosen = await _resolver.Resolve(Request(cargo, transport));

            Assert.That(chosen, Is.EqualTo(transport), "drive the boat before the payload decides");
        }

        private ChooseUnitToActivateRequest Request(params DataBinding<UnitData>[] options) =>
            new ChooseUnitToActivateRequest(_us,
                options.Select(o => new SelectionRequest<UnitData>.ValidOption(o, o.GetValue().Name)).ToList(),
                new List<SelectionRequest<UnitData>.InvalidOption>());

        private static Weapon Rifle() => new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, Weapon weapon,
            float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.5f,
                    weapons: new List<Weapon> { weapon },
                    initialPosition: new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{atX},{atZ}", quality: 4, defense: 4,
                modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
