using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #290 — the advance-and-shoot gate must be measured against the allowance that was IN FORCE for the
    // move, not one re-derived afterwards.
    //
    // Reported from play: Robot Legions' "Inspiring Bots" grants Rapid Advance (+4" Advance) as a one-shot
    // (NextTrigger) rule. Cast on a Slow unit (-2" Advance, -4" Rush/Charge) that makes its Advance 8" —
    // the same as its reduced Charge — and the move resolver correctly allowed and coloured an 8" advance.
    // But ExecuteMoveStage spends one-shot movement grants the moment the move resolves, so when
    // ChooseActionStage re-derived the allowance it asked "what could this unit advance NOW", got the
    // un-granted 4", and refused to offer Shoot.
    //
    // The fix records the allowance with the distance. These tests pin both halves: the recording, and the
    // fact that a re-derivation would still be wrong (so the old approach cannot quietly come back).
    [TestFixture]
    public class MoveShootAllowanceTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // The exact reported scenario, end to end through the real stages.
        [Test]
        public async Task SlowUnitWithGrantedRapidAdvance_MayStillShootAfterItsEightInchAdvance()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            World world = World.Build(_store, capturer);
            AttachSlow(world.Unit);
            GrantOnce(world.Unit, "Rapid Advance");

            var moveCtx = new MovementActionContext(world.Ctx, world.Unit);
            Assert.That(moveCtx.MaxAdvanceDistance, Is.EqualTo(8f).Within(0.001f),
                "6\" base, -2\" Slow, +4\" granted Rapid Advance");
            Assert.That(moveCtx.MaxChargeDistance, Is.EqualTo(8f).Within(0.001f),
                "12\" base, -4\" Slow - the advance and charge distances coincide, as reported");

            await MoveAndReconcile(world, moveCtx, distanceInches: 8f);

            Assert.That(world.UnitCtx.MoveShootAllowance, Is.EqualTo(8f).Within(0.001f),
                "the allowance recorded is the one the move was authorised against");

            await DriveChooseAction(world);
            Assert.That(capturer.Captured!.ValidOptions, Does.Contain(ChooseActionStage.SHOOT_CHOICE_NAME),
                "an 8\" move within an 8\" Advance allowance is an ADVANCE - the unit may still shoot");
        }

        // The trap, stated directly: after the move the grant is gone, so a fresh query gives a smaller
        // number than the move was actually allowed. Any future code that re-derives the allowance at
        // shoot time reintroduces the bug, and this test says so.
        [Test]
        public async Task AfterTheMove_ARederivedAllowanceIsSmallerThanTheOneTheMoveUsed()
        {
            World world = World.Build(_store, new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));
            AttachSlow(world.Unit);
            GrantOnce(world.Unit, "Rapid Advance");

            var moveCtx = new MovementActionContext(world.Ctx, world.Unit);
            await MoveAndReconcile(world, moveCtx, distanceInches: 8f);

            float rederived = MovementRuleQueries.EffectiveMoveShootDistance(
                world.Unit.GetValue(), world.Ctx.RuleEvaluator);

            Assert.That(rederived, Is.EqualTo(4f).Within(0.001f),
                "the one-shot grant is spent by the move, so a fresh query sees only Slow");
            Assert.That(world.UnitCtx.MoveShootAllowance, Is.GreaterThan(rederived),
                "which is exactly why the gate must not re-derive it");
        }

        // The gate still bites when it should: the same Slow unit with no grant may only advance 4", so a
        // 6" move is a Rush and shooting is off. Without this the fix could be "never blocks anything".
        [Test]
        public async Task SlowUnitWithoutTheGrant_CannotShootAfterOvershootingItsAdvance()
        {
            // The enemy sits just past where the 6" move ends, so Charge stays available: with Shoot
            // blocked and Move spent, a lone Pass is NOT prompted (the stage ends the activation instead),
            // and there would be no menu to inspect.
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.CHARGE_CHOICE_NAME);
            World world = World.Build(_store, capturer, enemyPosition: new Position(18.5f, 10f));
            AttachSlow(world.Unit);

            var moveCtx = new MovementActionContext(world.Ctx, world.Unit);
            Assert.That(moveCtx.MaxAdvanceDistance, Is.EqualTo(4f).Within(0.001f));

            await MoveAndReconcile(world, moveCtx, distanceInches: 6f);

            await DriveChooseAction(world);
            Assert.That(capturer.Captured, Is.Not.Null, "the menu must have been raised to be inspected");
            Assert.That(capturer.Captured!.ValidOptions, Does.Not.Contain(ChooseActionStage.SHOOT_CHOICE_NAME),
                "6\" is past a Slow unit's 4\" Advance - that was a Rush, so no shooting");
            Assert.That(capturer.Captured!.InvalidOptions.Any(o =>
                o.Option == ChooseActionStage.SHOOT_CHOICE_NAME && o.Reason.Contains("max to move and shoot")),
                Is.True, "and the menu says why");
        }

        // A unit that has not moved is not measured at all - the gate only applies to a move that happened.
        [Test]
        public async Task AUnitThatHasNotMoved_IsNotGatedByTheAllowance()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            World world = World.Build(_store, capturer);
            AttachSlow(world.Unit);

            await DriveChooseAction(world);

            Assert.That(world.UnitCtx.HasMoved, Is.False);
            Assert.That(capturer.Captured!.ValidOptions, Does.Contain(ChooseActionStage.SHOOT_CHOICE_NAME),
                "standing still and shooting is always legal - a 0\" allowance must not gate a 0\" move");
        }

        // The recorded allowance is the MAX across models, matching the recorded distance (which is the max
        // over models, GetMaxMoveDistance). A joined Fast hero that ranges ahead within its own larger
        // Advance must not make the whole unit read as having rushed.
        [Test]
        public void MaxModelAdvanceDistance_TakesTheLargestModelBudget()
        {
            World world = World.Build(_store, new CapturingStringSelectionRequester("x"), extraModels: 1);
            AttachSlow(world.Unit);
            // The second model alone is Fast: +2" Advance on top of the unit's Slow -2".
            world.Unit.GetValue().ModelBindings[1].GetValue()
                .AttachRuleDefinition(new ResolvedRule("Fast", CoreRuleCatalog.Fast));

            var moveCtx = new MovementActionContext(world.Ctx, world.Unit);

            Assert.That(moveCtx.MaxAdvanceDistance, Is.EqualTo(4f).Within(0.001f),
                "the unit scalar is still Slow's 4\"");
            Assert.That(moveCtx.MaxModelAdvanceDistance, Is.EqualTo(6f).Within(0.001f),
                "but the Fast model may advance 6\", and the recorded distance is a max over models");
        }

        // ── harness ────────────────────────────────────────────────────────────────────────────────────

        // Commits a move of the given length through the REAL ExecuteMoveStage (which spends the one-shot
        // grant) and reconciles it into the unit action context through the REAL MovementStage hook, so the
        // production wiring - not a hand-written equivalent - is what the assertions above exercise.
        private static async Task MoveAndReconcile(World world, MovementActionContext moveCtx, float distanceInches)
        {
            Position start = world.Unit.GetValue().ModelBindings[0].GetValue().Position;
            moveCtx.SubmitValidPathTemplate(new List<ModelMoveEntry>
            {
                new ModelMoveEntry(world.Unit.GetValue().ModelBindings[0],
                    new List<Position> { new Position(start.x + distanceInches, start.z) }),
            });

            var execute = new ExecuteMoveStage(world.Ctx, new NoOpLayer<IMovementActionContext>());
            execute.OnMoveExecuted.Bind("done");
            await execute.Enter(moveCtx);

            new ReconcilableMovementStage(world.Ctx).Reconcile(world.UnitCtx, moveCtx);
        }

        private static async Task DriveChooseAction(World world)
        {
            var stage = new ChooseActionStage(world.Ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCast.Bind("Cast");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(world.UnitCtx);
        }

        // Exposes MovementStage's own reconcile hook so the test drives the production path that feeds
        // RegisterMoveFinished, rather than reimplementing it (which would pass even if the stage regressed).
        private sealed class ReconcilableMovementStage : MovementStage
        {
            public ReconcilableMovementStage(IGameContext ctx)
                : base(ctx, new NoOpLayer<IUnitActionContext>()) { }

            public void Reconcile(IUnitActionContext self, IMovementActionContext child) =>
                ReconcileChildContextBeforeLeaving(self, child);
        }

        private sealed class World
        {
            public TriggeredMoveTestContext Ctx = null!;
            public DataBinding<UnitData> Unit = null!;
            public UnitActionContext UnitCtx = null!;

            // A rifle-armed unit with an enemy well inside range, so Shoot's target check never decides
            // the outcome - only the move/shoot distance gate does.
            public static World Build(GameDataStore store, IPlayerRequestByID requester, int extraModels = 0,
                Position? enemyPosition = null)
            {
                var player = new PlayerID(Guid.NewGuid());
                var enemyPlayer = new PlayerID(Guid.NewGuid());
                store.Create(new TeamData(0, new List<PlayerID> { player }));
                store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

                var bindings = new List<DataBinding<ModelData>>();
                for (int i = 0; i <= extraModels; i++)
                {
                    var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
                    // A melee weapon too, so Charge is offerable when an enemy is close: the negative test
                    // needs SOME other valid action to keep the menu alive (a lone Pass is not prompted).
                    var fist = new Weapon("Fist", rangeInches: 0f, attacks: 1, armorPenetration: 0);
                    var model = new ModelData(0.75f, new List<Weapon> { rifle, fist },
                        new Position(10f, 10f + i * 1.5f), store);
                    bindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
                }
                var unit = new UnitData(player, "Robots", quality: 4, defense: 4, modelBindings: bindings);
                DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));
                store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unitBinding }));

                var enemyModel = new ModelData(0.75f, new List<Weapon>(),
                    enemyPosition ?? new Position(10f, 22f), store);
                var enemy = new UnitData(enemyPlayer, "Grunts", quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>>
                        { store.GetDataBinding<ModelData>(store.Create(enemyModel)) });
                store.Create(new ArmyData(enemyPlayer,
                    new List<DataBinding<UnitData>> { store.GetDataBinding<UnitData>(store.Create(enemy)) }));

                // The resolver is what lets a token-granted rule name resolve back to its definition.
                var ctx = new TriggeredMoveTestContext(store, requester,
                    ruleResolver: CoreRuleCatalog.CreateResolver());
                var unitCtx = new UnitActionContext(ctx, unitBinding);
                unitCtx.Reset(unitBinding);
                return new World { Ctx = ctx, Unit = unitBinding, UnitCtx = unitCtx };
            }
        }

        private static void AttachSlow(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slow", CoreRuleCatalog.Slow));

        // The shape a spell's AddRule(scope: NextTrigger) leaves on the target - a one-shot RuleGrant token.
        private static void GrantOnce(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.FirstTrigger(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.NextTrigger)));
    }
}
