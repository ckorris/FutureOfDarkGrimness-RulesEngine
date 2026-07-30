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
    // Vertical-slice integration test for #197 Instinctive's compelled-attack primitive: "When this model
    // is activated, if it is able to shoot/charge an enemy unit, then it must immediately attack the
    // closest valid target and gets +1 to hit rolls for that attack."
    //
    // The mechanism is rule-agnostic. Effect.CompelClosestTarget answers the capability query ("carries
    // such a rule"), but the LIVE obligation is decided once, when the unit activates: ChooseActionStage
    // stamps TokenType.CompelledToAttack if the unit could attack at that moment, and everything
    // downstream reads the token. A unit that could not attack then is untouched for the whole activation
    // - it moves and attacks normally, free target, no bonus (owner's rules clarification 2026-07-31; the
    // first cut re-derived the compulsion per menu visit and got this backwards).
    //
    // While compelled: the menu collapses to the attack actions (both kinds when both apply - the rule
    // compels the target, not which attack), and the two choosers narrow to the closest VALID target
    // before the request is issued, so every resolver - human or AI - has no non-compliant option (the P20
    // Quick Shot marks pattern). The +1 rider is authored data gated on the same token plus the combat
    // kind, so it rides the compelled shot and charge swing, never a strike-back ("+1 for THAT attack").
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
        public async Task ChooseAction_NothingAttackableAtActivation_ActsFreely()
        {
            // "WHEN THIS MODEL IS ACTIVATED, if it is able to shoot/charge": unable at activation means
            // the rule does nothing at all this activation - a normal move, a normal attack.
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Far", atX: 90f); // 80" - far outside the rifle

            await DriveChooseAction(ctx, mob, bindPass: true);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME));
            Assert.That(mob.GetValue().Tokens.HasToken(TokenType.CompelledToAttack), Is.False,
                "no obligation was stamped, so nothing downstream restricts this activation");
        }

        // The clarification's whole point (owner, 2026-07-31): the condition is read at ACTIVATION, not
        // continuously. A unit that could not attack when it activated, then MOVED into range, attacks
        // like any other unit - free target choice, and no +1. Re-deriving the compulsion per menu visit
        // (the first cut) got this backwards, so this is the pin that keeps it fixed.
        [Test]
        public async Task ChooseAction_MovedIntoRangeAfterActivating_IsNotCompelled()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            DataBinding<UnitData> near = MakeEnemy("Near", atX: 90f);
            DataBinding<UnitData> far = MakeEnemy("Far", atX: 95f);

            // Activation 1: nothing in range, so no obligation is stamped.
            UnitActionContext unitCtx = await DriveChooseAction(ctx, mob, bindPass: true);
            Assert.That(mob.GetValue().Tokens.HasToken(TokenType.CompelledToAttack), Is.False);

            // Now it moves and both enemies are in range - the menu must NOT collapse, and the shot must
            // not be narrowed to the closest. TRANSLATE the models (keeping their z spread) rather than
            // stacking them on one point: stacked, the near enemy's base occludes the far one for every
            // model, the far row becomes unfireable on its own merits, and the target-gating assertion
            // below would pass no matter what the gate did.
            foreach (IModel model in mob.GetValue().Models)
            {
                ((ModelData)model).SetPosition(new Position(model.Position.x + 65f, model.Position.z));
            }
            unitCtx.RegisterMoveFinished(6f, 6f);

            var second = new RecordingActionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            var ctx2 = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: second);
            var stage = new ChooseActionStage(ctx2, new NoOpLayer<IUnitActionContext>());
            stage.ToShoot.Bind("test-shoot");
            await stage.Enter(unitCtx);

            Assert.That(second.OfferedOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME),
                "it moved into range under its own steam - the rule never bound, so idling is still legal");
            Assert.That(mob.GetValue().Tokens.HasToken(TokenType.CompelledToAttack), Is.False,
                "and no obligation appears late");

            var shootRequester = new CapturingShootRequester();
            var ctx3 = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: shootRequester);
            await DriveShootChooser(ctx3, mob);
            Assert.That(ReasonFor(shootRequester, far), Is.Null,
                "the farther target stays selectable: an uncompelled unit picks freely");
        }

        [Test]
        public async Task ChooseAction_CompelledAtActivation_StampsTheObligation_ForTheChoosers()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: requester);
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f);
            MakeEnemy("Near", atX: 15f);

            await DriveChooseAction(ctx, mob, bindShoot: true);

            Assert.That(mob.GetValue().Tokens.HasToken(TokenType.CompelledToAttack), Is.True,
                "the decision outlives the menu - the target choosers and the +1 rider both read it");
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
            Compel(mob);
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
            Compel(mob);
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
            Compel(mob);
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
            Compel(mob);
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

        [Test]
        public void Rider_WithoutTheObligation_NoBonus()
        {
            // The control the clarification demands: a unit that HAS Instinctive but was not compelled
            // this activation (it walked into range) shoots at its ordinary Quality.
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            DataBinding<UnitData> mob = MakeCompelledShooter(atX: 10f); // rule attached, token NOT stamped
            DataBinding<UnitData> enemy = MakeEnemy("Enemy", atX: 15f);

            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.EvaluateAll(
                new HitRollModifierContext(mob.GetValue(), enemy.GetValue(), 5f, AttackerMoved: true,
                    IsMelee: false, IsCharging: false),
                RuleParticipant.Actor(mob.GetValue(), null, mob.GetValue().Models));

            Assert.That(ops.OfType<RuleOperation.ApplyRollModifier>().Where(op => op.Roll == ERollKind.Hit)
                .Sum(op => op.Delta), Is.EqualTo(0),
                "'+1 for THAT attack' - an uncompelled attack is not that attack");
        }

        // ── The authored shape (mirrors what ships in the supplement) ────────────────────────────────────

        private static SpecialRuleDefinition Definition() => new SpecialRuleDefinition(RULE_NAME,
            new[]
            {
                // The compulsion: a capability answer, gated on the whole unit carrying the rule (#267).
                new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.AllModelsHaveThisRule(),
                    new Effect.CompelClosestTarget(), ELifetime.UntilEndOfGame),
                // The rider: "+1 to hit rolls FOR THAT ATTACK" - the compelled attack only, so both
                // entries gate on the obligation token as well as the combat kind. The unit's own shot
                // qualifies; the melee swing only when charging (a strike-back is not "that attack").
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.TokenPresent(TokenType.CompelledToAttack),
                        new Condition.Not(new Condition.IsMelee())),
                    new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.UntilEndOfGame),
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.And(new Condition.TokenPresent(TokenType.CompelledToAttack),
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

        // Stamps the obligation ChooseActionStage would have stamped at activation, for the tests that
        // drive a stage BELOW the menu (the choosers, the rider).
        private static void Compel(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(new Rules.Tokens.Token(TokenType.CompelledToAttack, 1,
                new TokenClearTrigger.ActivationEnd()));

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
