using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — Aircraft. The facets covered HERE are the -12" range and Flying's terrain/unit ignore; the -1 to
    // hit, the can't-be-moved-in-contact-with guard (pile-in, consolidation) and the 3.6 wording are in
    // AircraftContactGuardTests (#427), forced movement is in ForcedAircraftMoveTests, objectives in
    // ObjectiveOwnershipTests, the charge guard in MoveThroughEnemyValidationTests.
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
