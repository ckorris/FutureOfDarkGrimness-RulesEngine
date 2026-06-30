using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — Aircraft (PARTIAL). The ENFORCED facets are the defensive shooting (enemies get -12" range and -1
    // to hit) plus Flying's terrain/unit ignore. The DEFERRED facets (forced straight-line movement, can't be
    // charged, can't seize objectives, deploy-first) need new machinery and are NOT covered here.
    [TestFixture]
    public class AircraftRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public void Aircraft_ZeroesAShortRangeWeapon_MakingItEffectivelyImmune()
        {
            DataBinding<UnitData> attacker = MakeUnit(withAircraft: false);
            DataBinding<UnitData> aircraft = MakeUnit(withAircraft: true);
            var rifle = new Weapon("Rifle", rangeInches: 12f, attacks: 1, armorPenetration: 0);

            float effective = RangeRuleQueries.EffectiveRange(attacker.GetValue(), rifle, aircraft.GetValue(), _ctx.RuleEvaluator);

            Assert.That(effective, Is.EqualTo(0f),
                "Aircraft's -12\" range zeroes a 12\" weapon — nothing can be within 0\" without contact, so it's immune.");
        }

        [Test]
        public void Aircraft_IgnoresAllTerrain_AndMovesThroughUnits()
        {
            DataBinding<UnitData> aircraft = MakeUnit(withAircraft: true);

            Assert.That(MovementRuleQueries.IgnoresAllTerrain(aircraft.GetValue(), _ctx.RuleEvaluator), Is.True);
            Assert.That(MovementRuleQueries.CanMoveThroughEnemies(aircraft.GetValue(), _ctx.RuleEvaluator), Is.True);
        }

        [Test]
        public void Aircraft_IsCatalogued_AndResolvable()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            Assert.That(CoreRuleCatalog.All.Any(r => r.Name == "Aircraft"), Is.True, "Aircraft must be in All.");
            Assert.That(resolver.TryResolve("Aircraft", out _), Is.True);
        }

        private DataBinding<UnitData> MakeUnit(bool withAircraft)
        {
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0, 0), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (withAircraft)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            return binding;
        }
    }
}
