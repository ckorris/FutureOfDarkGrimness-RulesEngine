using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P20 Quick Shot - "This model may shoot after using Rush actions." The gate it waives is
    // ChooseActionStage's advance-and-shoot distance cap, which until now only a hardcoded Aircraft check
    // could bypass.
    //
    // The corpus ships three forms and they are NOT the same permission:
    //   * Quick Shot / Quick Shot Aura - the unit's own, unconditional: it may shoot anything.
    //   * Quick Shot Mark - "pick one enemy unit, which friendly units get Quick Shot AGAINST once". The
    //     permission is bound to that enemy, so a rushed unit may shoot the marked target and nothing else.
    //     Without the target narrowing the mark would license a free shot at anything AND never be spent,
    //     since a mark is only claimed when the attack lands on the unit carrying it.
    [TestFixture]
    public class QuickShotRuleIntegrationTests
    {
        private const string QuickShot = "Quick Shot";

        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;

        // The shipped shapes, hand-built because the engine suite cannot read the app's rule supplement.
        // QuickShotAndUnwieldyShippedDataTests asserts the authored definitions match these.
        private static readonly SpecialRuleDefinition QuickShotRule = new(QuickShot,
            new[]
            {
                new HookEntry(EHookID.Activation_OnActionChoice,
                    new Condition.Always(),
                    new Effect.ShootAfterRush(),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>());

        private static readonly SpecialRuleDefinition QuickShotAura = new("Quick Shot Aura",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                    new Condition.Always(),
                    new Effect.Aura(QuickShot),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>());

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(QuickShotRule);
            _resolver.Register(QuickShotAura);
        }

        // ---- The unit's own permission ---------------------------------------------------------------

        // Read through the gate itself rather than the menu: a rushed unit with nothing left to do never
        // reaches the menu at all (zero valid options auto-pass, and rushing also closes Pass), so there
        // would be no request to inspect. The tests where shooting IS unlocked drive the real menu below.
        [Test]
        public void AUnitThatRushed_CannotShoot()
        {
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));
            Rush(world);

            bool canShoot = ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out string reason);

            Assert.That(canShoot, Is.False,
                "the baseline: a unit that moved past its advance-and-shoot allowance may not shoot.");
            Assert.That(reason, Does.Contain("max to move and shoot"));
        }

        [Test]
        public async Task QuickShot_LetsARushedUnitShoot()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            World world = BuildWorld(capturer);
            world.Shooter.GetValue().AttachRuleDefinition(new ResolvedRule(QuickShot, QuickShotRule));
            Rush(world);

            await DriveChooseAction(world);

            Assert.That(ShootIsOffered(capturer), Is.True, "'may shoot after using Rush actions'.");
        }

        [Test]
        public async Task QuickShotAura_ConfersThePermissionOnTheWholeUnit()
        {
            // Every corpus reference is the Aura form (5 of the 5 Quick Shot refs), so a broken aura link
            // makes the rule unreachable in play even though the base rule works.
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            World world = BuildWorld(capturer);
            world.Shooter.GetValue().AttachRuleDefinition(
                new ResolvedRule("Quick Shot Aura", QuickShotAura));
            UnitCreationRules.Apply(world.Shooter.GetValue(), world.Context.RuleEvaluator);
            Rush(world);

            await DriveChooseAction(world);

            Assert.That(ShootIsOffered(capturer), Is.True, "'This model and its unit get Quick Shot.'");
        }

        [Test]
        public void QuickShot_IsAPermission_NotABiggerAdvance()
        {
            // Authored as a movement bonus instead, the unit could ADVANCE 12" - a different rule, and one
            // every "after Advance" condition would then read differently.
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));
            float before = MovementRuleQueries.EffectiveMoveShootDistance(
                world.Shooter.GetValue(), world.Context.RuleEvaluator);

            world.Shooter.GetValue().AttachRuleDefinition(new ResolvedRule(QuickShot, QuickShotRule));

            Assert.That(MovementRuleQueries.EffectiveMoveShootDistance(
                    world.Shooter.GetValue(), world.Context.RuleEvaluator),
                Is.EqualTo(before), "the advance allowance is untouched - only the shoot gate changes.");
        }

        // ---- The mark: same permission, bound to one enemy --------------------------------------------

        [Test]
        public async Task AQuickShotMarkOnAnEnemy_LetsARushedUnitShootIt()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            World world = BuildWorld(capturer);
            Mark(world.NearEnemy, QuickShot);
            Rush(world);

            await DriveChooseAction(world);

            Assert.That(ShootIsOffered(capturer), Is.True,
                "'friendly units get Quick Shot against it once' - the shot at the marked unit is legal.");
        }

        [Test]
        public void AMarkGrantingSomethingElse_DoesNotUnlockTheShot()
        {
            // Structural detection: it is the shoot-after-rush EFFECT that permits the shot, not the mere
            // presence of a mark. Shred Mark must not double as a Quick Shot.
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));
            Mark(world.NearEnemy, "Shred");
            Rush(world);

            Assert.That(ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out _), Is.False);
        }

        [Test]
        public void AMarkedTargetIsTheONLYFireableOne_ForARushedUnit()
        {
            // The narrowing itself, read through the shared gating pipeline the shoot STAGE also runs
            // (#200 - the action gate and the stage must never disagree about what is fireable).
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(
                    world.Shooter, world.Context, markedTargetsOnly: true), Is.False,
                "no marks on the table: a rushed unit has nothing it may legally shoot.");

            Mark(world.FarEnemy, QuickShot);

            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(
                    world.Shooter, world.Context, markedTargetsOnly: true), Is.True,
                "the marked enemy is in range and line of sight, so the shot is on.");
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(
                    world.Shooter, world.Context, markedTargetsOnly: false), Is.True,
                "...and without the narrowing everything in range stays fireable, as before.");
        }

        [Test]
        public async Task ARushedUnitWithAMark_MayNotShootTheUNMARKEDEnemy()
        {
            // The clause that makes the mark a real restriction rather than a free shot. Both enemies are
            // in range and line of sight; only the marked one may be selected.
            var capturer = new CapturingRangedAttackRequester();
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME),
                requesterForShooting: capturer);
            Mark(world.NearEnemy, QuickShot);
            Rush(world);

            await DriveChooseRangedAttack(world, markedTargetsOnly: true);

            ChooseRangedAttackRequest request = capturer.Captured!;
            var stats = request.WeaponOptions.SelectMany(o => o.WeaponTargetStats).ToList();

            Assert.That(Selectable(stats, world.NearEnemy), Is.True, "the marked enemy may be shot.");
            Assert.That(Selectable(stats, world.FarEnemy), Is.False,
                "the unmarked enemy may not - the permission was 'against' the marked unit only.");
        }

        [Test]
        public async Task WithoutRushing_AMarkRestrictsNothing()
        {
            // A mark on the table must not narrow a unit that never needed the permission.
            var capturer = new CapturingRangedAttackRequester();
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME),
                requesterForShooting: capturer);
            Mark(world.NearEnemy, QuickShot);

            await DriveChooseRangedAttack(world, markedTargetsOnly: false);

            var stats = capturer.Captured!.WeaponOptions.SelectMany(o => o.WeaponTargetStats).ToList();
            Assert.That(Selectable(stats, world.FarEnemy), Is.True,
                "a unit that stood still shoots whatever it likes.");
        }

        [Test]
        public void ReadingTheMark_DoesNotSpendIt()
        {
            // The mark is claimed by DetermineHitRollStage when the attack lands. Spending it while merely
            // asking "may I shoot?" would burn it on a browse the player then cancelled.
            World world = BuildWorld(new CapturingStringSelectionRequester(ChooseActionStage.PASS_CHOICE_NAME));
            Mark(world.NearEnemy, QuickShot);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(ShootAfterRushRules.MarkGrantsShootAfterRush(
                    world.NearEnemy.GetValue(), _resolver), Is.True);
            }

            Assert.That(world.NearEnemy.GetValue().Tokens.GetAllTokens(TokenType.Mark).Count(), Is.EqualTo(1),
                "the mark is still there for the attack that actually claims it.");
        }

        // ---- Helpers ----------------------------------------------------------------------------------

        private sealed record World(TriggeredMoveTestContext Context, UnitActionContext UnitContext,
            DataBinding<UnitData> Shooter, DataBinding<UnitData> NearEnemy, DataBinding<UnitData> FarEnemy);

        private static bool ShootIsOffered(CapturingStringSelectionRequester capturer) =>
            capturer.Captured!.ValidOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME);

        private static bool Selectable(IEnumerable<ChooseRangedAttackRequest.WeaponTargetStats> stats,
            DataBinding<UnitData> target) =>
            stats.Any(s => s.TargetUnit.Reference.Equals(target.Reference)
                && s.UnselectableReason == null && s.modelsThatCanShoot.Count > 0);

        // A Rush: moved further than the advance-and-shoot allowance, which is what closes the shoot gate.
        private static void Rush(World world) =>
            world.UnitContext.RegisterMoveFinished(distance: 11f,
                advanceAllowanceInches: GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);

        private void Mark(DataBinding<UnitData> enemy, string ruleName) =>
            enemy.GetValue().Tokens.AddToken(new Token(TokenType.Mark, 1,
                new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.ThisAttack)));

        private static async Task DriveChooseAction(World world)
        {
            var stage = new ChooseActionStage(world.Context, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCast.Bind("Cast");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(world.UnitContext);
        }

        private static async Task DriveChooseRangedAttack(World world, bool markedTargetsOnly)
        {
            var stage = new ChooseRangedAttackStage(world.Context, new NoOpLayer<ICombatActionContext>());
            stage.OnChoseWeapon.Bind("Fire");
            stage.BackToChooseAction.Bind("Back");
            stage.OnNoValidShots.Bind("Done");

            var combat = new CombatActionContext(world.Context, world.Shooter, isMelee: false,
                attackerMoved: true, markedTargetsOnly: markedTargetsOnly);
            await stage.Enter(combat);
        }

        // One rifle-armed shooter and two enemies, both inside the 24" range with clear line of sight, so
        // "which of the two may be shot" is decided purely by the marking - never by geometry.
        private World BuildWorld(IPlayerRequestByID requester, IPlayerRequestByID? requesterForShooting = null)
        {
            var shooterPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { shooterPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            DataBinding<UnitData> shooter = MakeUnit(shooterPlayer, "Shooter", new Position(10, 10), rifle);
            _store.Create(new ArmyData(shooterPlayer, new List<DataBinding<UnitData>> { shooter }));

            DataBinding<UnitData> near = MakeUnit(enemyPlayer, "Near Enemy", new Position(10, 18));
            DataBinding<UnitData> far = MakeUnit(enemyPlayer, "Far Enemy", new Position(20, 18));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { near, far }));

            var ctx = new TriggeredMoveTestContext(_store, requesterForShooting ?? requester,
                ruleResolver: _resolver);
            var unitCtx = new UnitActionContext(ctx, shooter);
            unitCtx.Reset(shooter);
            return new World(ctx, unitCtx, shooter, near, far);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position position,
            params Weapon[] weapons)
        {
            var model = new ModelData(0.75f, weapons.ToList(), position, _store);
            var unit = new UnitData(player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                    { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Captures the shoot menu and cancels, so the stage exits without needing a fire pipeline.
    internal sealed class CapturingRangedAttackRequester : IPlayerRequestByID
    {
        public ChooseRangedAttackRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is ChooseRangedAttackRequest r)
            {
                Captured = r;
                return Task.FromResult((TReply)(object)
                    new Cancelled<ChooseRangedAttackRequest.RangedAttackChoice>());
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
