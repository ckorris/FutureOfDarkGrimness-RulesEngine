using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #102 — the RangeModifier family. Increased Shooting Range (+6" to the bearer's own weapons, Actor seat)
    // and Ranged Shrouding (enemies get -6" range shooting this unit, Subject seat) fold via
    // RangeRuleQueries.EffectiveRangeDelta into ChooseRangedAttackStage's target-eligibility check. Models are
    // base radius 0.5", so base-to-base distance = centre distance - 1.0"; a 12" rifle reaches 13" of centre.
    [TestFixture]
    public class RangeModifierRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _attackerPlayer;
        private PlayerID _enemyPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _attackerPlayer = new PlayerID(Guid.NewGuid());
            _enemyPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _attackerPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { _enemyPlayer }));
        }

        [Test]
        public void IncreasedShootingRange_BringsAnOutOfRangeTargetIntoRange()
        {
            // Enemy centre 14" → 13" base-to-base, just beyond the 12" rifle.
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            MakeArmyUnit(_enemyPlayer, new Position(14, 0, 0));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.False,
                "13\" base-to-base is beyond the rifle's 12\" range.");

            Attach(attacker, "Increased Shooting Range", CoreRuleCatalog.IncreasedShootingRange);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.True,
                "+6\" range (18\" effective) now reaches the 13\" target.");
        }

        [Test]
        public void RangedShrouding_OnDefender_PushesAnInRangeTargetOutOfRange()
        {
            // Enemy centre 8" → 7" base-to-base, comfortably within the 12" rifle.
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> enemy = MakeArmyUnit(_enemyPlayer, new Position(8, 0, 0));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.True,
                "7\" base-to-base is within the rifle's 12\" range.");

            Attach(enemy, "Ranged Shrouding", CoreRuleCatalog.RangedShrouding);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.False,
                "the defender's -6\" range (6\" effective) no longer reaches the 7\" target.");
        }

        [Test]
        public void EffectiveRangeDelta_FoldsAttackerBuffAndDefenderDebuff()
        {
            var weapon = new Weapon("Rifle", rangeInches: 12f, attacks: 1, armorPenetration: 0);
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> defender = MakeArmyUnit(_enemyPlayer, new Position(8, 0, 0));
            RuleEvaluator ev = _ctx.RuleEvaluator;

            Assert.That(RangeRuleQueries.EffectiveRangeDelta(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(0), "no rules → no delta.");

            Attach(attacker, "Increased Shooting Range", CoreRuleCatalog.IncreasedShootingRange);
            Assert.That(RangeRuleQueries.EffectiveRangeDelta(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(6), "attacker's +6 buff applies at the Actor seat.");

            Attach(defender, "Ranged Shrouding", CoreRuleCatalog.RangedShrouding);
            Assert.That(RangeRuleQueries.EffectiveRangeDelta(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(0), "defender's -6 debuff (Subject seat) nets against the +6 buff.");
        }

        // The Army-Creator picker derives from CoreRuleCatalog.All; an uncatalogued rule is unpickable and a
        // no-op. Guards that the family (base rules + auras) is registered and resolvable.
        [Test]
        public void RangeRules_AreCatalogued_AndResolvable()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (string name in new[] { "Increased Shooting Range", "Ranged Shrouding",
                                            "Increased Shooting Range Aura", "Ranged Shrouding Aura" })
            {
                Assert.That(CoreRuleCatalog.All.Any(r => r.Name == name), Is.True, $"{name} must be in All.");
                Assert.That(resolver.TryResolve(name, out _), Is.True, $"{name} must resolve.");
            }
        }

        private static void Attach(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private static Weapon Rifle(float range) => new Weapon("Rifle", range, attacks: 1, armorPenetration: 0);

        private DataBinding<UnitData> MakeArmyUnit(PlayerID player, Position pos, params Weapon[] weapons)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: weapons.ToList(),
                initialPosition: pos, gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(player, "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> unitBinding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));
            return unitBinding;
        }
    }
}
