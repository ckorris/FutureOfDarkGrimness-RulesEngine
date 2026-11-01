using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #102 — the RangeModifier family. Increased Shooting Range (+6" to the bearer's own weapons, Actor seat)
    // and Ranged Shrouding (enemies get -4" range shooting this unit since 3.6, Subject seat) fold via
    // RangeRuleQueries.EffectiveRange into ChooseRangedAttackStage's target-eligibility check. Models are
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
            // Resolver-equipped so the #377 range-mark peek can resolve a mark's rule by name.
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
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
            // Enemy centre 10" → 9" base-to-base, within the 12" rifle.
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> enemy = MakeArmyUnit(_enemyPlayer, new Position(10, 0, 0));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.True,
                "9\" base-to-base is within the rifle's 12\" range.");

            Attach(enemy, "Ranged Shrouding", CoreRuleCatalog.RangedShrouding);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.False,
                "the defender's 3.6 -4\" range (8\" effective) no longer reaches the 9\" target.");
        }

        [Test]
        public void EffectiveRange_FoldsAttackerBuffAndDefenderDebuff()
        {
            var weapon = new Weapon("Rifle", rangeInches: 12f, attacks: 1, armorPenetration: 0);
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> defender = MakeArmyUnit(_enemyPlayer, new Position(8, 0, 0));
            RuleEvaluator ev = _ctx.RuleEvaluator;

            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(12f), "no rules → base range.");

            Attach(attacker, "Increased Shooting Range", CoreRuleCatalog.IncreasedShootingRange);
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(18f), "attacker's +6 buff applies at the Actor seat.");

            Attach(defender, "Ranged Shrouding", CoreRuleCatalog.RangedShrouding);
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), weapon, defender.GetValue(), ev),
                Is.EqualTo(14f), "defender's 3.6 -4 debuff (Subject seat) nets against the +6 buff.");
        }

        [Test]
        public void RangedShrouding_Floor_StopsTheReductionAtSixInches()
        {
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0));
            DataBinding<UnitData> defender = MakeArmyUnit(_enemyPlayer, new Position(8, 0, 0));
            Attach(defender, "Ranged Shrouding", CoreRuleCatalog.RangedShrouding);
            RuleEvaluator ev = _ctx.RuleEvaluator;

            // 9" weapon: 9 - 4 = 5, but the rule floors the result at 6".
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), Rifle(9f), defender.GetValue(), ev),
                Is.EqualTo(6f), "the -4 reduction is floored at the rule's 6\" minimum.");

            // 24" weapon: 24 - 4 = 20, comfortably above the floor.
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), Rifle(24f), defender.GetValue(), ev),
                Is.EqualTo(20f), "the floor doesn't apply when the reduced range is already above it.");

            // #432: "to a min. of 6\"" limits the reduction - it never lengthens a gun already shorter than 6".
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), Rifle(4f), defender.GetValue(), ev),
                Is.EqualTo(4f), "a 4\" weapon keeps its 4\" against the rule; the floor does not raise it to 6\".");
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

        // --- #377 range-extension marks: "friendly units get +6\" range when shooting against it once" (#439: the mark
        // names Increased Shooting Range, the rule "+6\" range on their weapons when shooting" stands for) ---

        [Test]
        public void RangeMark_OnDefender_ExtendsRangeAgainstIt_WithoutSpendingTheMark()
        {
            var weapon = new Weapon("Rifle", rangeInches: 12f, attacks: 1, armorPenetration: 0);
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> marked = MakeArmyUnit(_enemyPlayer, new Position(14, 0, 0));
            DataBinding<UnitData> unmarked = MakeArmyUnit(_enemyPlayer, new Position(30, 0, 0));
            RuleEvaluator ev = _ctx.RuleEvaluator;

            marked.GetValue().Tokens.AddToken(new Token(TokenType.Mark, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Increased Shooting Range",
                    ELifetime.ThisAttack)));

            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), weapon, marked.GetValue(), ev),
                Is.EqualTo(18f), "the mark's +6\" folds into range against the MARKED target.");
            Assert.That(RangeRuleQueries.EffectiveRange(attacker.GetValue(), weapon, unmarked.GetValue(), ev),
                Is.EqualTo(12f), "the extension is target-bound - an unmarked enemy gets base range.");
            Assert.That(marked.GetValue().Tokens.HasToken(TokenType.Mark), Is.True,
                "the range check is a peek - the mark is claimed later, at the hit-roll stage.");
        }

        [Test]
        public void RangeMark_MakesAnOutOfRangeMarkedTargetFireable()
        {
            // Enemy centre 14" -> 13" base-to-base, beyond the 12" rifle - exactly the shot the
            // Eternal Guidance / Clearview Leaves spells exist to enable.
            DataBinding<UnitData> attacker = MakeArmyUnit(_attackerPlayer, new Position(0, 0, 0), Rifle(12f));
            DataBinding<UnitData> enemy = MakeArmyUnit(_enemyPlayer, new Position(14, 0, 0));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.False,
                "13\" base-to-base is beyond the rifle's 12\" range.");

            enemy.GetValue().Tokens.AddToken(new Token(TokenType.Mark, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Increased Shooting Range",
                    ELifetime.ThisAttack)));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(attacker, _ctx), Is.True,
                "the mark's +6\" (18\" effective) makes the marked target fireable.");
        }

        private static void Attach(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private static Weapon Rifle(float range) => new Weapon("Rifle", range, attacks: 1, armorPenetration: 0);

        // A plain unit (no army) for the MovementActionContext charge-distance test.
        private DataBinding<UnitData> MakeMovementUnit()
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "MoveUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

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
