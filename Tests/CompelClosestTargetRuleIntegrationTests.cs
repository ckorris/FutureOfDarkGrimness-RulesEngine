using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 Instinctive's compelled-attack primitive (slice 1: the
    // from-here half): "When this model is activated, if it is able to shoot/charge an enemy unit, then it
    // must immediately attack the closest valid target and gets +1 to hit rolls for that attack."
    //
    // The mechanism is rule-agnostic - Effect.CompelClosestTarget answers the capability query, and three
    // sites read it: ChooseActionStage collapses the menu to the attack actions while one is possible
    // (owner ruling 2026-07-30, strict "must immediately attack"), and the two target choosers narrow their
    // lists to the closest VALID target before the request is issued, so every resolver - human or AI -
    // simply has no non-compliant option (the P20 Quick Shot marks pattern). The +1 rider is ordinary
    // authored data (RollModifier entries); what this file pins about it is the condition combo: it rides
    // the unit's own shot and its charge swing, never its strike-back ("+1 for THAT attack").
    [TestFixture]
    public class CompelClosestTargetRuleIntegrationTests
    {
        private const string RULE_NAME = "Instinctive";

        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // ── The capability ───────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Capability_UnitWithTheRule_AnswersWithTheRuleName()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(_us, "Mob", models: 3, atX: 10f);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(RULE_NAME, Definition()));

            Assert.That(CapabilityRuleQueries.MustAttackClosestSource(unit.GetValue(), ctx.RuleEvaluator),
                Is.EqualTo(RULE_NAME), "a unit-attached rule covers every model, so the gate holds");
        }

        [Test]
        public void Capability_JoinedModelWithoutTheRule_FreesTheUnit()
        {
            // The #267 convention: the compulsion binds the WHOLE unit's action, so it is authored gated on
            // AllModelsHaveThisRule - per-model attachment with one model lacking it (a joined hero) must
            // answer null, or one goblin's instinct would compel the hero it is escorting.
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(_us, "Mob", models: 3, atX: 10f);
            ResolvedRule rule = new ResolvedRule(RULE_NAME, Definition());
            unit.GetValue().Models.Cast<ModelData>().Take(2).ToList()
                .ForEach(m => m.AttachRuleDefinition(rule));

            Assert.That(CapabilityRuleQueries.MustAttackClosestSource(unit.GetValue(), ctx.RuleEvaluator),
                Is.Null, "one model without the rule - the unit is not compelled");
        }

        // ── ChooseActionStage: the menu collapses while an attack is possible ────────────────────────────

        [Test]
        public async Task ChooseAction_AbleToShoot_OnlyTheAttackIsOffered()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Near", atX: 15f); // 5" - inside the 24" rifle

            await DriveChooseAction(ctx, mob, bindShoot: true);

            Assert.That(requester.OfferedOptions, Is.EquivalentTo(new[] { ChooseActionStage.SHOOT_CHOICE_NAME }),
                "able to attack -> attacking is all it may do (no Move, no Pass)");
            Assert.That(requester.OfferedInvalidOptions.Select(o => o.Option),
                Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(requester.OfferedInvalidOptions
                    .Single(o => o.Option == ChooseActionStage.MOVEMENT_CHOICE_NAME).Reason,
                Does.Contain(RULE_NAME), "the reason names the compelling rule");
            Assert.That(requester.OfferedInvalidOptions.Select(o => o.Option),
                Does.Contain(ChooseActionStage.PASS_CHOICE_NAME));
        }

        [Test]
        public async Task ChooseAction_AbleToCharge_ChargeStaysOffered()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.CHARGE_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledBrawler(atX: 10f);
            MakeEnemy("Adjacent", atX: 11.5f); // 1.5" - inside the 2" melee cylinder

            await DriveChooseAction(ctx, mob, bindCharge: true);

            Assert.That(requester.OfferedOptions, Is.EquivalentTo(new[] { ChooseActionStage.CHARGE_CHOICE_NAME }),
                "a melee-only compelled unit in contact range may only Charge");
        }

        [Test]
        public async Task ChooseAction_NothingAttackable_ActsFreely()
        {
            // Slice 1 boundary: with no attack possible from here, the unit is unconstrained. (The
            // move-to-attack extension - "if a move could enable one, it must make it" - is the next
            // slice; this pins that the compulsion alone never strands a unit with no legal option.)
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 90f); // 80" - far outside the rifle

            await DriveChooseAction(ctx, mob, bindPass: true);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME));
        }

        [Test]
        public async Task ChooseAction_WithoutTheRule_MenuUnchanged()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> unit = MakeUnit(_us, "Plain", models: 3, atX: 10f, weapon: Rifle());
            MakeEnemy("Near", atX: 15f);

            await DriveChooseAction(ctx, unit, bindPass: true);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "control: an uncompelled unit that can shoot may still move instead");
        }

        // ── The ranged chooser: closest VALID target ─────────────────────────────────────────────────────

        [Test]
        public async Task RangedChooser_OnlyTheClosestFireableTarget_IsSelectable()
        {
            var requester = new CapturingShootRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            DataBinding<UnitData> near = MakeEnemy("Near", atX: 16f); // 6"
            DataBinding<UnitData> far = MakeEnemy("Far", atX: 22f);   // 12"

            await DriveShootChooser(ctx, mob);

            Assert.That(ReasonFor(requester, near), Is.Null, "the closest fireable target stays selectable");
            Assert.That(ReasonFor(requester, far), Does.Contain(RULE_NAME),
                "every farther target is gated, with the compelling rule named");
        }

        [Test]
        public async Task RangedChooser_ClosestMeansClosestVALID_NotClosestOnTheTable()
        {
            // The nearest enemy hides behind a blocking wall - "the closest valid target" is the closest
            // one the unit may actually shoot, so the farther, visible enemy must stay selectable (gating
            // on the absolute nearest would leave the request with nothing fireable and livelock the AI
            // through the #200 gate).
            var requester = new CapturingShootRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            DataBinding<UnitData> hidden = MakeEnemy("Hidden", atX: 16f);  // 6" but behind the wall
            DataBinding<UnitData> visible = MakeEnemy("Visible", atX: 22f, atZ: 30f); // ~15.6", open
            _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(13f, 14f, 8f, 12f)));

            await DriveShootChooser(ctx, mob);

            Assert.That(ReasonFor(requester, visible), Is.Null,
                "the closest target the unit CAN shoot stays selectable");
            Assert.That(StatsFor(requester, hidden).modelsThatCanShoot, Is.Empty,
                "the walled-off enemy is unfireable on its own merits, not via the compulsion");
        }

        [Test]
        public async Task RangedChooser_WithoutTheRule_NothingIsGated()
        {
            var requester = new CapturingShootRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> unit = MakeUnit(_us, "Plain", models: 3, atX: 10f, weapon: Rifle());
            DataBinding<UnitData> near = MakeEnemy("Near", atX: 16f);
            DataBinding<UnitData> far = MakeEnemy("Far", atX: 22f);

            await DriveShootChooser(ctx, unit);

            Assert.That(ReasonFor(requester, near), Is.Null);
            Assert.That(ReasonFor(requester, far), Is.Null, "control: no compulsion, no narrowing");
        }

        // ── The melee chooser ────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task MeleeChooser_OnlyTheClosestDefender_IsValid()
        {
            var requester = new CapturingMeleeRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledBrawler(atX: 10f);
            DataBinding<UnitData> close = MakeEnemy("Close", atX: 11.2f);   // 0.2" base to base
            DataBinding<UnitData> nearEdge = MakeEnemy("Edge", atX: 12.6f); // 1.6" - in range, farther

            await DriveMeleeChooser(ctx, mob);

            Assert.That(requester.Captured!.ValidOptions.Select(o => o.Name), Is.EquivalentTo(new[] { "Close" }));
            Assert.That(requester.Captured!.InvalidOptions.Single(o => o.Name == "Edge").Reason,
                Does.Contain(RULE_NAME));
        }

        // ── The +1 rider's condition combo ───────────────────────────────────────────────────────────────

        [TestCase(false, false, 1, TestName = "Rider_PlusOne_OnTheShot")]
        [TestCase(true, true, 1, TestName = "Rider_PlusOne_OnTheChargeSwing")]
        [TestCase(true, false, 0, TestName = "Rider_NoBonus_OnTheStrikeBack")]
        public void Rider_AppliesToTheCompelledAttack_NotTheStrikeBack(bool isMelee, bool isCharging,
            int expectedDelta)
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            DataBinding<UnitData> enemy = MakeEnemy("Enemy", atX: 15f);

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new HitRollModifierContext(mob.GetValue(), enemy.GetValue(), 5f, AttackerMoved: false,
                    IsMelee: isMelee, IsCharging: isCharging),
                RuleParticipant.Actor(mob.GetValue(), null, mob.GetValue().Models));

            int delta = ops.OfType<RuleOperation.ApplyRollModifier>()
                .Where(op => op.Roll == ERollKind.Hit).Sum(op => op.Delta);
            Assert.That(delta, Is.EqualTo(expectedDelta),
                "'+1 to hit rolls for THAT attack' - the unit's own shot and charge swing, never its strike-back");
        }

        // ── Slice 2: the move-to-attack half ─────────────────────────────────────────────────────────────

        [Test]
        public void Planner_EnemyJustBeyondRifleRange_FindsAnAdvanceThatEnablesTheShot()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f); // ~28" - rifle is 24", advance is 6"

            CompelledAttackMovePlanner.EnablingMoves moves =
                CompelledAttackMovePlanner.FindEnablingMoves(ctx, mob);

            Assert.That(moves.ShootMove, Is.Not.Null, "a ~4.3in advance brings the enemy into range");
            Assert.That(moves.ChargeMove, Is.Null, "27in to contact is far beyond the 12in charge cap");
            Assert.That(CompelledAttackMovePlanner.WouldEndAbleToAttack(ctx, mob, moves.ShootMove!,
                GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES), Is.True,
                "the found move satisfies the same predicate the resolvers enforce");
            Assert.That(mob.GetValue().Models.First().Position.x, Is.EqualTo(10f).Within(0.01f),
                "the probe restored the real positions - detection must not move anything");
        }

        [Test]
        public void Planner_MeleeOnlyUnit_FindsAMoveIntoChargeContact()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> freaks = MakeCompelledBrawler(atX: 10f);
            MakeEnemy("Prey", atX: 18f); // 8" away - inside the 12" charge cap

            CompelledAttackMovePlanner.EnablingMoves moves =
                CompelledAttackMovePlanner.FindEnablingMoves(ctx, freaks);

            Assert.That(moves.ChargeMove, Is.Not.Null, "a ~6in move ends in the melee cylinder");
            Assert.That(moves.ShootMove, Is.Null, "no ranged weapons - no shoot-enabling move exists");
            Assert.That(CompelledAttackMovePlanner.WouldEndAbleToAttack(ctx, freaks, moves.ChargeMove!,
                GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES), Is.True);
        }

        [Test]
        public void Planner_EnemyOutOfAllReach_FindsNothing()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Distant", atX: 100f); // 90" - no move enables anything

            CompelledAttackMovePlanner.EnablingMoves moves =
                CompelledAttackMovePlanner.FindEnablingMoves(ctx, mob);

            Assert.That(moves.ShootMove, Is.Null);
            Assert.That(moves.ChargeMove, Is.Null);
        }

        [Test]
        public void Planner_WouldEndAbleToAttack_RejectsAMoveThatEndsShort()
        {
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f);

            List<ModelMoveEntry> tooShort = mob.GetValue().ModelBindings
                .Select(mb => new ModelMoveEntry(mb, new List<Position>
                    { new Position(mb.GetValue().Position.x + 1f, mb.GetValue().Position.z) }))
                .ToList();

            Assert.That(CompelledAttackMovePlanner.WouldEndAbleToAttack(ctx, mob, tooShort,
                GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES), Is.False,
                "1in toward a 28in-away enemy leaves it out of range - the predicate must say so");
        }

        [Test]
        public async Task ChooseAction_MoveCanEnableTheAttack_AutoOptionOffered_PassBarred()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f);

            UnitActionContext unitCtx = await DriveChooseAction(ctx, mob, bindMove: true);

            Assert.That(requester.OfferedOptions,
                Does.Contain(RULE_NAME + ChooseActionStage.COMPELLED_MOVE_AND_SHOOT_SUFFIX),
                "the planner's found move surfaces as its own auto-resolve action");
            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "the manual Move stays available (requirement-flagged, enforced by the resolvers)");
            Assert.That(requester.OfferedInvalidOptions
                    .Single(o => o.Option == ChooseActionStage.PASS_CHOICE_NAME).Reason,
                Does.Contain(RULE_NAME), "idling is barred while a move could enable the attack");
            Assert.That(unitCtx.MoveMustEndAbleToAttackSource, Is.EqualTo(RULE_NAME),
                "the manual move carries the requirement to the movement request");
        }

        [Test]
        public async Task ChooseAction_NoEnablingMoveExists_PassStaysAvailable()
        {
            // The livelock guard: barred idling is only legal while a REAL alternative exists. With every
            // enemy out of reach the planner finds nothing, so the unit acts freely.
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Distant", atX: 100f);

            UnitActionContext unitCtx = await DriveChooseAction(ctx, mob, bindPass: true);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME));
            Assert.That(unitCtx.MoveMustEndAbleToAttackSource, Is.Null,
                "no obligation - an ordinary move must not be destination-restricted");
        }

        [Test]
        public async Task ChooseAction_AutoOptionChosen_StashesThePlannedMove_AndRoutesToMovement()
        {
            string label = RULE_NAME + ChooseActionStage.COMPELLED_MOVE_AND_SHOOT_SUFFIX;
            var requester = new RecordingActionRequester(label);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f);

            UnitActionContext unitCtx = await DriveChooseAction(ctx, mob, bindMove: true);

            Assert.That(unitCtx.PendingPlannedMove, Is.Not.Null,
                "accepting the auto option hands the planner's validated move to the movement flow");
        }

        [Test]
        public async Task DefinePathStage_PlannedMove_IsSubmittedWithoutRaisingTheRequest()
        {
            // A requester that THROWS on any request: proves the planned move bypasses the prompt, and
            // fails fast (not a hang) if a regression falls through to it.
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                playerRequester: new ThrowingRequester());
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f);

            CompelledAttackMovePlanner.EnablingMoves moves =
                CompelledAttackMovePlanner.FindEnablingMoves(ctx, mob);
            var movementCtx = new MovementActionContext(ctx, mob, plannedMove: moves.ShootMove);

            bool pathDefined = false;
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("test-defined");
            stage.OnPathDefined.OnWillActivate += _ => pathDefined = true;
            stage.BackToChooseAction.Bind("test-back");
            await stage.Enter(movementCtx);

            Assert.That(pathDefined, Is.True, "the pre-validated move is submitted directly");
            Assert.That(movementCtx.TryGetPaths(out _), Is.True);
        }

        [Test]
        public async Task DefinePathStage_ManualCompelledMove_RequestCarriesTheRuleName()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f);

            var movementCtx = new MovementActionContext(ctx, mob, mustEndAbleToAttackRule: RULE_NAME);

            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("test-defined");
            stage.BackToChooseAction.Bind("test-back");
            await stage.Enter(movementCtx);

            Assert.That(requester.Captured, Is.Not.Null);
            Assert.That(requester.Captured!.MustEndAbleToAttackRule, Is.EqualTo(RULE_NAME),
                "the resolvers read this to enforce the destination (slice 3)");
        }

        // ── Slice 3: the resolver-side destination check (request data + table geometry, no IGameContext) ─

        [Test]
        public async Task DestinationCheck_AgreesWithTheGates_OnAllThreeOutcomes()
        {
            var requester = new CapturingMoveRequester();
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 38f); // ~28": in range after a ~4.3" advance; in melee after ~27"

            // Capture a real request (budgets, profiles, overrides) by driving DefinePathStage.
            var movementCtx = new MovementActionContext(ctx, mob, mustEndAbleToAttackRule: RULE_NAME);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("test-defined");
            stage.BackToChooseAction.Bind("test-back");
            await stage.Enter(movementCtx);
            DefineMovementPathRequest request = requester.Captured!;

            List<ModelMoveEntry> Move(float dx) => mob.GetValue().ModelBindings
                .Select(mb => new ModelMoveEntry(mb, new List<Position>
                    { new Position(mb.GetValue().Position.x + dx, mb.GetValue().Position.z) }))
                .ToList();

            Assert.That(CompelledMoveDestinationCheck.EndsAbleToAttack(ctx.TableState, request, Move(5f)),
                Is.True, "a 5in advance ends in rifle range - compliant");
            Assert.That(CompelledMoveDestinationCheck.EndsAbleToAttack(ctx.TableState, request, Move(1f)),
                Is.False, "a 1in shuffle ends out of range of everything - non-compliant");
            Assert.That(CompelledMoveDestinationCheck.EndsAbleToAttack(ctx.TableState, request, Move(10f)),
                Is.False, "10in is a RUSH - in range but unable to shoot, and not in melee range: non-compliant");
        }

        // ── The authored shape (mirrors what ships in the supplement) ────────────────────────────────────

        private static SpecialRuleDefinition Definition() => new SpecialRuleDefinition(RULE_NAME,
            new[]
            {
                // The compulsion: a capability answer, gated on the whole unit carrying the rule (#267).
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.AllModelsHaveThisRule(),
                    new Effect.CompelClosestTarget(), ELifetime.UntilEndOfGame),
                // The rider, split by combat kind: the unit's own shot always qualifies (its shot IS the
                // compelled attack while the rule binds), the melee swing only when charging - a
                // strike-back is not "that attack".
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.AllModelsHaveThisRule(),
                        new Condition.Not(new Condition.IsMelee())),
                    new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.UntilEndOfGame),
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.AllModelsHaveThisRule(),
                        new Condition.And(new Condition.IsMelee(), new Condition.IsCharging())),
                    new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: "Must attack the closest valid target when able, at +1 to hit.");

        // ── Drivers ──────────────────────────────────────────────────────────────────────────────────────

        private async Task<UnitActionContext> DriveChooseAction(IGameContext ctx, DataBinding<UnitData> unit,
            bool bindShoot = false, bool bindCharge = false, bool bindPass = false, bool bindMove = false)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);

            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            if (bindShoot) stage.ToShoot.Bind("test-shoot");
            if (bindCharge) stage.ToCharge.Bind("test-charge");
            if (bindPass) stage.ToReconcileEndOfActivation.Bind("test-pass");
            if (bindMove) stage.ToMovement.Bind("test-move");
            await stage.Enter(unitCtx);
            return unitCtx;
        }

        private async Task DriveShootChooser(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var combatCtx = new CombatActionContext(ctx, unit, isMelee: false);
            var stage = new ChooseRangedAttackStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnChoseWeapon.Bind("test-chose");
            stage.BackToChooseAction.Bind("test-back");
            stage.OnNoValidShots.Bind("test-none");
            await stage.Enter(combatCtx);
        }

        private async Task DriveMeleeChooser(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var combatCtx = new CombatActionContext(ctx, unit, isMelee: true);
            var stage = new ChooseMeleeDefenderStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnDefenderChosen.Bind("test-chosen");
            stage.BackToChooseAction.Bind("test-back");
            await stage.Enter(combatCtx);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────────

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Claws() => new Weapon("Claws", 0f, 2, 0);

        private DataBinding<UnitData> MakeCompelledShooter(float atX)
        {
            DataBinding<UnitData> unit = MakeUnit(_us, "Mob", models: 3, atX: atX, weapon: Rifle());
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(RULE_NAME, Definition()));
            return unit;
        }

        private DataBinding<UnitData> MakeCompelledBrawler(float atX)
        {
            DataBinding<UnitData> unit = MakeUnit(_us, "Freaks", models: 3, atX: atX, weapon: Claws());
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(RULE_NAME, Definition()));
            return unit;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, int models, float atX,
            float atZ = 10f, Weapon? weapon = null)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var weapons = weapon == null ? new List<Weapon>() : new List<Weapon> { new Weapon(
                    weapon.Name, weapon.RangeInches, weapon.Attacks, weapon.ArmorPenetration) };
                var model = new ModelData(0.5f, weapons, new Position(atX, atZ + i * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (!_store.GetAllValues<ArmyData>().Any(a => a.PlayerID == owner))
            {
                _store.Create(new TeamData(owner == _us ? 0 : 1, new List<PlayerID> { owner }));
                _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            }
            else
            {
                _store.GetAllDataBindings<ArmyData>().First(a => a.GetValue().PlayerID == owner)
                    .GetValue().UnitBindings.Add(binding);
            }
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(string name, float atX, float atZ = 10f)
            => MakeUnit(_them, name, models: 1, atX: atX, atZ: atZ, weapon: Claws());

        private static ChooseRangedAttackRequest.WeaponTargetStats StatsFor(CapturingShootRequester requester,
            DataBinding<UnitData> target)
            => requester.Captured!.WeaponOptions.Single().WeaponTargetStats
                .Single(s => s.TargetUnit.Reference.Equals(target.Reference));

        private static string? ReasonFor(CapturingShootRequester requester, DataBinding<UnitData> target)
            => StatsFor(requester, target).UnselectableReason;

        // ── Test doubles ─────────────────────────────────────────────────────────────────────────────────

        private sealed class CapturingShootRequester : IPlayerRequestByID
        {
            public ChooseRangedAttackRequest? Captured { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseRangedAttackRequest ranged)
                {
                    Captured = ranged;
                    return Task.FromResult((TReply)(object)(CancellableResult<ChooseRangedAttackRequest.RangedAttackChoice>)
                        new Cancelled<ChooseRangedAttackRequest.RangedAttackChoice>());
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }

        private sealed class ThrowingRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
                => throw new InvalidOperationException("No request should have been raised: " + request.GetType());
        }

        private sealed class CapturingMoveRequester : IPlayerRequestByID
        {
            public DefineMovementPathRequest? Captured { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is DefineMovementPathRequest move)
                {
                    Captured = move;
                    return Task.FromResult((TReply)(object)(CancellableResult<List<ModelMoveEntry>>)
                        new Cancelled<List<ModelMoveEntry>>());
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }

        private sealed class CapturingMeleeRequester : IPlayerRequestByID
        {
            public ChooseMeleeDefenderRequest? Captured { get; private set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is ChooseMeleeDefenderRequest melee)
                {
                    Captured = melee;
                    return Task.FromResult((TReply)(object)(CancellableResult<DataBinding<UnitData>>)
                        new Cancelled<DataBinding<UnitData>>());
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
