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

        // Artillery is an attacker-seat (Actor) rule: +1 to hit beyond 9", which LOWERS the
        // threshold (easier). It reuses the distance the stage already computes — pure attach,
        // no new wiring beyond Stealth's.
        [Test]
        public async Task ArtilleryAttacker_BeyondNine_LowersHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(3),
                "base 4, lowered by 1 because Artillery gives +1 to hit beyond 9\".");
        }

        [Test]
        public async Task ArtilleryAttacker_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachArtillery(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery's distance condition fails within 9\", so no modifier is applied.");
        }

        // Artillery's defensive facet: as a TARGET, enemies shooting it from beyond 9" take -2 to
        // hit (Subject seat), which RAISES the threshold. Mirrors Stealth's defender-seat distance
        // modifier on the same OnHitRollModifier evaluation — proves Artillery's second HookEntry fires.
        [Test]
        public async Task ArtilleryDefender_BeyondNine_RaisesAttackerHitThreshold()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(6),
                "base 4, raised by 2 because Artillery gives enemies -2 to hit it from beyond 9\".");
        }

        [Test]
        public async Task ArtilleryDefender_WithinNine_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(NearPos);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Artillery's distance condition fails within 9\", so the -2 defensive modifier is not applied.");
        }

        // Both facets at once: an Artillery attacker (+1 → -1 threshold) shooting an Artillery defender
        // (-2 → +2 threshold) from beyond 9". The Actor and Subject entries both fire and stack: 4 - 1 + 2 = 5.
        [Test]
        public async Task ArtilleryBothSeats_BeyondNine_StackActorAndSubject()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachArtillery(attacker);
            AttachArtillery(defender);

            DetermineHitRollResults result = await RunStage(attacker, defender);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "Artillery attacker +1 (−1 threshold) and Artillery defender −2 (+2 threshold) stack: 4 − 1 + 2 = 5.");
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

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(5),
                "base 4, raised by 1 because Indirect applies -1 to hit after the attacker moved.");
        }

        [Test]
        public async Task IndirectAttacker_WithoutMoving_NoChange()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: false);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect's AfterMoving condition fails when the attacker held, so no modifier is applied.");
        }

        // Indirect's -1 is gated on Not(IsMelee): a charge sets HasMoved, so without the gate an Indirect
        // carrier would wrongly take -1 on its melee swings. In melee (even after moving) the penalty must
        // not apply — it's a shooting rule.
        [Test]
        public async Task IndirectAttacker_InMelee_NoPenalty()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(FarPos);
            AttachIndirect(attacker);

            DetermineHitRollResults result = await RunStage(attacker, defender, attackerMoved: true, isMelee: true);

            Assert.That(result.HitRollNeeded, Is.EqualTo(4),
                "Indirect is a shooting rule; its -1-after-moving must not apply to a melee (charge) swing.");
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

        private static void AttachArtillery(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Artillery", CoreRuleCatalog.Artillery));

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
