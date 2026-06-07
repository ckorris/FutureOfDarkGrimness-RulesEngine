using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Stealth's -1-to-hit flows through the
    // REAL DetermineHitRollNeededStage. The stage fires the OnHitRollModifier "when", the
    // RuleEvaluator evaluates both seats, and the RollModifierSink folds the result into the
    // hit threshold — none of it interpreted by the stage. Stealth is a Subject-seat rule
    // gated on distance > 9". Units are quality 4, so the base hit roll needed is 4.
    [TestFixture]
    public class HitRollRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position FarPos      = new Position(20, 5);  // ~18.5" base-to-base → > 9"
        private static readonly Position NearPos     = new Position(3, 5);   // ~1.5" → ≤ 9"

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        [Test]
        public async Task DefenderWithStealth_BeyondNine_RaisesHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(defender);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 because Stealth applies -1 to hit from beyond 9\".");
        }

        [Test]
        public async Task DefenderWithStealth_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachStealth(defender);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth's distance condition fails within 9\", so no modifier is applied.");
        }

        [Test]
        public async Task AttackerWithStealth_DoesNotFire_SeatMismatch()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(attacker); // wrong seat: Stealth is Subject-only

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth is a Subject-seat rule; it must not fire when its bearer is the attacker.");
        }

        [Test]
        public async Task NoRules_BaselineUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no rules → just the attacker's quality.");
        }

        // Artillery is an attacker-seat (Actor) rule: +1 to hit beyond 9", which LOWERS the
        // threshold (easier). It reuses the distance the stage already computes — pure attach,
        // no new wiring beyond Stealth's.
        [Test]
        public async Task ArtilleryAttacker_BeyondNine_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by 1 because Artillery gives +1 to hit beyond 9\".");
        }

        [Test]
        public async Task ArtilleryAttacker_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachArtillery(attacker);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery's distance condition fails within 9\", so no modifier is applied.");
        }

        // Indirect is an attacker-seat rule: -1 to hit when the unit moved this activation,
        // which RAISES the threshold. The condition reads AttackerMoved off the metadata —
        // this exercises the HasMoved → CombatMetadata threading, not just a distance check.
        [Test]
        public async Task IndirectAttacker_AfterMoving_RaisesHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender, attackerMoved: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 because Indirect applies -1 to hit after the attacker moved.");
        }

        [Test]
        public async Task IndirectAttacker_WithoutMoving_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender, attackerMoved: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect's AfterMoving condition fails when the attacker held, so no modifier is applied.");
        }

        // Reliable is an attacker-seat rule that FLOORS the base quality to 2+ before per-roll
        // modifiers — a different sink (QualityFloorSink) than the modifier rules above, but riding
        // the same OnHitRollModifier evaluation. Units are quality 4, so Reliable improves 4 → 2.
        [Test]
        public async Task ReliableAttacker_FloorsBaseQualityToTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachReliable(attacker);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(2),
                "Reliable floors the attacker's base quality 4 to 2+.");
        }

        // "Still modifiable": Reliable floors the base to 2, then a defender's Stealth (-1 to hit
        // from beyond 9") stacks on top, raising the threshold to 3. Proves the floor sets the BASE,
        // not the final value.
        [Test]
        public async Task ReliableAttacker_WithStealthDefender_FloorThenModifierStacks()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachReliable(attacker);
            AttachStealth(defender);

            DetermineHitRollNeededResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base floored to 2 by Reliable, then +1 harder from Stealth's -1 to hit.");
        }

        private async Task<DetermineHitRollNeededResults> RunStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool attackerMoved = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollNeededStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0,
                specialRules: new HashSet<ISpecialRule_Weapon>());
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollNeededResults result), Is.True,
                "Stage must store a DetermineHitRollNeededResults in metadata.");
            return result;
        }

        private static void AttachStealth(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

        private static void AttachArtillery(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));

        private static void AttachIndirect(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));

        private static void AttachReliable(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                specialRules: new List<SpecialRule>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
