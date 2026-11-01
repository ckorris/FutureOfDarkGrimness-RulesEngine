using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Stealth's -1-to-hit flows through the
    // REAL DetermineHitRollStage. The stage fires the OnHitRollModifier "when", the
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
            // Resolver-equipped so granted-rule read-back resolves a claimed mark's rule by name.
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
        }

        [Test]
        public async Task DefenderWithStealth_BeyondNine_RaisesHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 because Stealth applies -1 to hit from beyond 9\".");
        }

        [Test]
        public async Task DefenderWithStealth_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachStealth(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth's distance condition fails within 9\", so no modifier is applied.");
        }

        [Test]
        public async Task AttackerWithStealth_DoesNotFire_SeatMismatch()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachStealth(attacker); // wrong seat: Stealth is Subject-only

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Stealth is a Subject-seat rule; it must not fire when its bearer is the attacker.");
        }

        [Test]
        public async Task NoRules_BaselineUnchanged()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no rules → just the attacker's quality.");
        }

        // Artillery's two seats (3.6: Hold + over 12" / unmoved + most models + over 12") moved to
        // ArtilleryRuleIntegrationTests with #425.

        // #425 - replaces IndirectAttacker_AfterMoving_RaisesHitThreshold. Core Rules v3.6.0 dropped Indirect's
        // "-1 to hit when shooting after moving": the rule now only grants its line-of-sight and Sight-Blocker-cover
        // facets on a Hold (IndirectLineOfSightRuleIntegrationTests). Shooting after an Advance is therefore at the
        // plain threshold - through the REAL stage with AttackerMoved threaded in, so a reintroduced penalty on any
        // hook this stage reads would show here. Melee (a charge also sets HasMoved) is unaffected either way.
        [Test]
        public async Task IndirectAttacker_AfterMoving_NoHitPenalty()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults shot = await RunStage(attacker, defender, attackerMoved: true);
            Assert.That(shot.HitRollNeeded, Is.EqualTo(4),
                "3.6: no -1 to hit after moving - an Indirect weapon that Advanced hits on the plain 4+.");

            DetermineHitRollResults swing = await RunStage(attacker, defender, attackerMoved: true, isMelee: true);
            Assert.That(swing.HitRollNeeded, Is.EqualTo(4), "and nothing in melee either.");
        }

        [Test]
        public async Task IndirectAttacker_WithoutMoving_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect never modifies the hit roll; on a Hold it changes what can be targeted and the cover, not the dice.");
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

            DetermineHitRollResults result = await RunStage(attacker, defender);

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

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base floored to 2 by Reliable, then +1 harder from Stealth's -1 to hit.");
        }

        // #015: the stage also determines the attack-dice count (weapon Attacks × weapons firing),
        // which RollToHitStage then rolls. Here a 2-attack weapon fired by 3 weapons → 6 dice.
        [Test]
        public async Task AttackCount_IsWeaponAttacksTimesWeaponCount()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 2, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 3);
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True);
            Assert.That(result.AttackCount, Is.EqualTo(6f),
                "attack count is weapon Attacks (2) × weapon count (3).");
        }

        // Evasive (Subject): -1 to hit against the bearer with no gate, so it raises the threshold in
        // BOTH shooting and melee — the defensive mirror of Precise.
        [Test]
        public async Task DefenderWithEvasive_RaisesHitThreshold_Shooting()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachEvasive(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Evasive gives enemies -1 to hit → +1 threshold (shooting).");
        }

        [Test]
        public async Task DefenderWithEvasive_RaisesHitThreshold_Melee()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachEvasive(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Evasive is un-gated, so the -1 to hit also applies to melee swings.");
        }

        // Melee Evasion (Subject): the same -1 but gated to melee — fires in melee, not shooting.
        [Test]
        public async Task DefenderWithMeleeEvasion_Melee_RaisesThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachMeleeEvasion(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Melee Evasion gives -1 to hit in melee → +1 threshold.");
        }

        [Test]
        public async Task DefenderWithMeleeEvasion_Shooting_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachMeleeEvasion(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender); // shooting

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Melee Evasion is melee-only (IsMelee gate); a shooting attack is unaffected.");
        }

        // Precise (Actor): +1 to hit, any attack → lowers the threshold.
        [Test]
        public async Task AttackerWithPrecise_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachPrecise(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "Precise gives +1 to hit → -1 threshold.");
        }

        // Good Shot (Actor): +1 to hit when shooting only — Not(IsMelee) gate.
        [Test]
        public async Task AttackerWithGoodShot_Shooting_LowersThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachGoodShot(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "Good Shot gives +1 to hit when shooting → -1 threshold.");
        }

        [Test]
        public async Task AttackerWithGoodShot_Melee_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachGoodShot(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Good Shot is shooting-only; a melee swing gets no bonus.");
        }

        // #100 #14 Mark/Tag: a mark on the defender (here naming "Precise") is CLAIMED by the first attack —
        // the marked rule transfers to the attacker for that attack (Precise = +1 to hit → threshold 4-1=3)
        // and the mark is removed, so a second attack into the now-unmarked enemy gets nothing.
        [Test]
        public async Task MarkedDefender_FirstAttack_ClaimsRule_ThenMarkConsumed()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            MarkDefender(defender, "Precise");

            DetermineHitRollResults first = await RunStage(attacker, defender);
            Assert.That(first.HitRollNeeded, Is.EqualTo(3),
                "the first attack claims the Precise mark — the granted +1 to hit lowers the threshold to 3.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Mark), Is.False,
                "the mark is spent by the attack that claimed it.");

            DetermineHitRollResults second = await RunStage(attacker, defender);
            Assert.That(second.HitRollNeeded, Is.EqualTo(4),
                "the mark was consumed; a later attack into the same enemy gets no bonus.");
        }

        // Control: no mark → no claimed rule, base threshold.
        [Test]
        public async Task UnmarkedDefender_NoClaim_BaselineThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4), "no mark on the defender → nothing claimed.");
        }

        // #377 — a one-shot range grant (Battle Rune: "+6\" range on its weapons when shooting once", granted as
        // Increased Shooting Range since #439) is spent by the
        // shooting attack that used it: the range check itself only peeks (a per-frame query must not
        // consume), so DetermineHitRollStage runs one live range evaluation per shooting attack. A melee
        // swing never read the range and must not spend it.
        [Test]
        public async Task RangeGrant_SpentByShootingAttack_NotByMelee()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            GrantRangeBonus(attacker);

            await RunStage(attacker, defender, isMelee: true);
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.True,
                "a melee swing never reads range - the one-shot range grant survives it.");

            await RunStage(attacker, defender);
            Assert.That(attacker.GetValue().Tokens.HasToken(TokenType.RuleGrant), Is.False,
                "the shooting attack is the range's 'next time' - the grant is spent.");
        }

        private static void GrantRangeBonus(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.FirstTrigger(),
                Payload: new TokenPayload.RuleGrant("Increased Shooting Range", ELifetime.NextTrigger)));

        // #377 — a mark naming a combat-kind-gated phrase rule ("+1 to hit rolls in melee", the Magic
        // Targeting / Raiding Drugs spells): the claim transfers the rule, and the rule's own gate then
        // decides whether it does anything. A melee swing gets the bonus; a shooting claim spends the
        // mark for nothing (#101 doctrine: forcing a wasted buff is legitimate).
        [Test]
        public async Task MarkedGatedHitRule_MeleeSwing_AppliesBonus()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            MarkDefender(defender, "+1 to hit rolls in melee");

            DetermineHitRollResults result = await RunStage(attacker, defender, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "the claimed \"+1 to hit rolls in melee\" fires on the melee swing: threshold 4 - 1 = 3.");
        }

        [Test]
        public async Task MarkedGatedHitRule_ShootingClaim_SpendsMarkForNothing()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            MarkDefender(defender, "+1 to hit rolls in melee");

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "shooting claims the mark but the melee gate fails - no bonus.");
            Assert.That(defender.GetValue().Tokens.HasToken(TokenType.Mark), Is.False,
                "the mark is spent by the claiming attack even though its gate failed.");
        }

        private static void MarkDefender(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.Mark, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.ThisAttack)));

        private async Task<DetermineHitRollResults> RunStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, bool attackerMoved = false,
            bool isMelee = false)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new DetermineHitRollStage<ICombatMetadata>(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1, attackerMoved, isMelee);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineHitRollResults result), Is.True,
                "Stage must store a DetermineHitRollResults in metadata.");
            return result;
        }

        private static void AttachStealth(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

        private static void AttachIndirect(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));

        private static void AttachReliable(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Reliable", CoreRuleCatalog.Reliable));

        private static void AttachEvasive(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Evasive", CoreRuleCatalog.Evasive));

        private static void AttachMeleeEvasion(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Melee Evasion", CoreRuleCatalog.MeleeEvasion));

        private static void AttachPrecise(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Precise", CoreRuleCatalog.Precise));

        private static void AttachGoodShot(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Good Shot", CoreRuleCatalog.GoodShot));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
