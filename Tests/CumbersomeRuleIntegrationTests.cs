using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Tests.RulesHarness;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #425 - Cumbersome (new in Core Rules v3.6.0, on every artillery unit): "May only use Hold or Advance
    // actions, but can't shoot when moving." Mirrors the RestrictActions/Immobile and Aircraft action-gating
    // tests: the menu (Charge greyed with the reason, Move kept but labelled Advance-only), the move budget
    // (Rush and the charge-length approach capped at the Advance, in the request AND the authoritative
    // re-validation), the shoot gate (a moved unit - a 0" Advance included, D1 - may not shoot; a Holding one
    // may), and the Tactician's legality (no charge plans, no shot credited to an Advance).
    [TestFixture]
    public class CumbersomeRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void Cumbersome_CarriesTheCore360Wording()
        {
            Assert.That(CoreRuleCatalog.Cumbersome.Description,
                Is.EqualTo("May only use Hold or Advance actions, but can't shoot when moving."));
            Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Cumbersome", out _), Is.True,
                "a core rule - army load resolves the book name against the catalog.");
        }

        [Test]
        public void Cumbersome_AtActionChoice_RestrictsToHoldOrAdvance_AndForbidsShootingAfterMoving()
        {
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Cumbersome);
            IUnit unit = harness.BuildUnit("P1", modelCount: 1, "Cumbersome");

            var ops = harness.EvaluateAll(new ActionChoiceContext(unit), RuleParticipant.Actor(unit));

            ops.HasOperation<RuleOperation.RestrictActions>(op =>
                op.Allowed.Count == 2 && op.Allowed.Contains(EActionType.Hold) && op.Allowed.Contains(EActionType.Advance));
            ops.HasOperation<RuleOperation.ForbidShootingAfterMoving>();
            Assert.That(ActionRestrictionQueries.ShootingAfterMovingForbiddenBy(unit, harness.Evaluator),
                Is.EqualTo("Cumbersome"), "attributed by name for the menu's reason text.");
            Assert.That(ActionRestrictionQueries.MayDeclare(unit, harness.Evaluator, EActionType.Rush), Is.False);
            Assert.That(ActionRestrictionQueries.MayDeclare(unit, harness.Evaluator, EActionType.Charge), Is.False);
            Assert.That(ActionRestrictionQueries.MayDeclare(unit, harness.Evaluator, EActionType.Advance), Is.True);
        }

        // The action set: an enemy 2.5" away (inside the 3" strike range, outside the 1" standoff) would offer
        // Charge; Cumbersome greys it with the reason. Move stays (it is the Advance) and says so; Shoot is the
        // Hold and stays offered.
        [Test]
        public async Task ChooseAction_CumbersomeUnit_KeepsMoveAndShoot_GreysChargeWithTheReason()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(13.5f, 10f));

            ChooseActionRequest req = await DriveChooseAction(world);

            Assert.That(req.ValidOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "'may only use Hold or Advance actions' - the Move action is the Advance.");
            Assert.That(req.ValidOptions, Does.Contain(ChooseActionStage.SHOOT_CHOICE_NAME), "Hold and shoot.");
            Assert.That(req.ValidOptions, Does.Not.Contain(ChooseActionStage.CHARGE_CHOICE_NAME));
            Assert.That(req.InvalidOptions.Any(o => o.Option == ChooseActionStage.CHARGE_CHOICE_NAME
                    && o.Reason == "Cumbersome - may only use Hold or Advance actions."), Is.True,
                "and the menu says why: " + string.Join(" | ", req.InvalidOptions.Select(o => $"{o.Option}={o.Reason}")));
            Assert.That(req.OptionDescriptions?[ChooseActionStage.MOVEMENT_CHOICE_NAME],
                Is.EqualTo("Advance only (Cumbersome) - no Rush, and can't shoot after moving."),
                "the menu has no Rush entry to grey, so Move carries the restriction as its subtext.");
        }

        [Test]
        public async Task ChooseAction_Control_WithoutCumbersome_OffersCharge()
        {
            World world = BuildWorld(cumbersome: false, enemyAt: new Position(13.5f, 10f));

            ChooseActionRequest req = await DriveChooseAction(world);

            Assert.That(req.ValidOptions, Does.Contain(ChooseActionStage.CHARGE_CHOICE_NAME),
                "control: the same geometry offers Charge to a unit without the rule.");
            Assert.That(req.OptionDescriptions == null || !req.OptionDescriptions.ContainsKey(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                Is.True, "and no Advance-only subtext.");
        }

        [Test]
        public void MoveBudget_IsAdvanceOnly_InTheContextAndTheSharedQueries()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(40f, 10f));
            IUnit unit = world.Unit.GetValue();

            var move = new MovementActionContext(world.Context, world.Unit);
            Assert.That(move.MaxAdvanceDistance, Is.EqualTo(GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES).Within(0.001f));
            Assert.That(move.MaxRushDistance, Is.EqualTo(move.MaxAdvanceDistance).Within(0.001f), "no Rush.");
            Assert.That(move.MaxChargeDistance, Is.EqualTo(move.MaxAdvanceDistance).Within(0.001f),
                "no charge-length approach either - the hard move cap is max(Rush, Charge).");
            move.TryGetModelMoveBudget(unit.Models[0], out float advance, out float rush, out float charge);
            Assert.That((rush, charge), Is.EqualTo((advance, advance)), "per-model budgets are capped the same way.");

            Assert.That(MovementRuleQueries.EffectiveMaxRushDistance(unit, world.Context.RuleEvaluator),
                Is.EqualTo(advance).Within(0.001f), "the Tactician's Rush budget agrees.");
            (float a, float r, float c) = MovementRuleQueries.PerModelMoveBudgets(unit, world.Context.RuleEvaluator)[unit.Models[0].ID];
            Assert.That((r, c), Is.EqualTo((a, a)), "and so do its per-model budgets.");
        }

        [Test]
        public void MoveRequest_CarriesTheAdvanceCap_AndNamesTheRule()
        {
            var requester = new CapturingMoveRequester();
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(40f, 10f), requester: requester);

            Assert.ThrowsAsync<CancelledMoveSentinel>(async () => await RunMovement(world));

            DefineMovementPathRequest req = requester.Captured!;
            Assert.That(req.MaxRushDistance, Is.EqualTo(req.MaxAdvanceDistance).Within(0.001f));
            Assert.That(req.MaxDistanceInches, Is.EqualTo(req.MaxAdvanceDistance).Within(0.001f),
                "the move panel's hard cap is the Advance.");
            Assert.That(req.AdvanceOnlyRule, Is.EqualTo("Cumbersome"));
            Assert.That(req.NoShootAfterMoveRule, Is.EqualTo("Cumbersome"));
        }

        [Test]
        public void MoveLongerThanTheAdvance_IsRejectedByTheAuthoritativeCheck()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(40f, -10f),
                requester: new ResolvingMoveRequester(9f));

            Assert.ThrowsAsync<RequestResponseInvalidException>(async () => await RunMovement(world),
                "a 9\" move is a Rush - DefinePathStage re-validates against the capped budget.");
        }

        [Test]
        public async Task AfterAnAdvance_CannotShoot()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(22f, 10f),
                requester: new ResolvingMoveRequester(3f));
            await RunMovement(world);
            Assert.That(world.UnitContext.HasMoved, Is.True);

            bool canShoot = ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out string reason);

            Assert.That(canShoot, Is.False, "'can't shoot when moving' - an Advance is move-only.");
            Assert.That(reason, Is.EqualTo("Cumbersome - can't shoot when moving."));
        }

        // D1 (owner review): the Advance ACTION is the move. A 0" Advance ("Skip all" / Done with no waypoint)
        // spends the move exactly as MovementStage records it (#333), so it forfeits the shot too.
        [Test]
        public async Task AfterAZeroInchAdvance_StillCannotShoot()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(22f, 10f),
                requester: new ResolvingMoveRequester(0f));
            await RunMovement(world);
            Assert.That(world.UnitContext.HasMoved, Is.True, "a 0\" move still spends the move action.");

            Assert.That(ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out string reason), Is.False);
            Assert.That(reason, Does.Contain("can't shoot when moving"));
        }

        [Test]
        public void Holding_CanShoot()
        {
            World world = BuildWorld(cumbersome: true, enemyAt: new Position(22f, 10f));

            Assert.That(ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out string reason), Is.True,
                "a Cumbersome unit that has not moved is Holding, and Hold can shoot: " + reason);
        }

        [Test]
        public async Task Control_WithoutCumbersome_ShootsAfterAnAdvance()
        {
            World world = BuildWorld(cumbersome: false, enemyAt: new Position(22f, 10f),
                requester: new ResolvingMoveRequester(3f));
            await RunMovement(world);

            Assert.That(ChooseActionStage.GetCanShoot(world.Context, world.UnitContext, out _), Is.True,
                "control: an ordinary unit keeps its advance-and-shoot.");
        }

        // --- Tactician legality ----------------------------------------------------------------------------

        [Test]
        public void Tactician_GeneratesNoChargePlans_AndNoMoveBeyondTheAdvance()
        {
            var tableState = new TableState(_store);
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            var us = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> gun = MakeUnit(us, "Field Artillery", new Position(20f, 20f),
                new Weapon("Cannon", rangeInches: 36f, attacks: 1, armorPenetration: 2),
                new Weapon("Crew", rangeInches: 0f, attacks: 1, armorPenetration: 0));
            gun.GetValue().AttachRuleDefinition(new ResolvedRule("Cumbersome", CoreRuleCatalog.Cumbersome));
            MakeUnit(new PlayerID(Guid.NewGuid()), "Grunts", new Position(26f, 20f),
                new Weapon("Blade", rangeInches: 0f, attacks: 2, armorPenetration: 0));
            _store.Create(new ObjectiveData(new Position(20f, 31f), _store)); // 11" off - a Rush target for others

            List<MacroAction> actions = MacroActionGenerator.Enumerate(evaluator, tableState, gun);

            Assert.That(actions.Any(a => a.Intent == EMacroIntent.ChargeToContact), Is.False,
                "'may only use Hold or Advance actions' - no charge or charge-approach is ever planned.");
            foreach (MacroAction action in actions)
            {
                float longest = action.Move.Count == 0 ? 0f : action.Move.Max(MoveLength);
                Assert.That(longest, Is.LessThanOrEqualTo(GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES + 0.01f),
                    $"{action.Rationale}: every plan fits the Advance-only request it will be checked against.");
            }
        }

        [Test]
        public void Tactician_CreditsNoShotToAnAdvance_ButKeepsTheHoldVolley()
        {
            float ScoreFor(bool cumbersome, EMacroIntent intent, EActionType actionType)
            {
                SetUp();
                var tableState = new TableState(_store);
                var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
                var us = new PlayerID(Guid.NewGuid());
                var them = new PlayerID(Guid.NewGuid());
                DataBinding<UnitData> gun = MakeUnit(us, "Gun", new Position(20f, 24f),
                    new Weapon("Cannon", rangeInches: 36f, attacks: 4, armorPenetration: 1));
                DataBinding<UnitData> target = MakeUnit(them, "Target", new Position(44f, 24f),
                    new Weapon("Rifle", rangeInches: 12f, attacks: 1, armorPenetration: 0));
                _store.Create(new ArmyData(us, new List<DataBinding<UnitData>> { gun }));
                _store.Create(new ArmyData(them, new List<DataBinding<UnitData>> { target }));
                if (cumbersome)
                    gun.GetValue().AttachRuleDefinition(new ResolvedRule("Cumbersome", CoreRuleCatalog.Cumbersome));
                var planner = new TacticianPlanner(tableState, evaluator);
                planner.BeginActivation(gun);
                MacroAction candidate = MacroActionGenerator.Enumerate(evaluator, tableState, gun)
                    .First(a => a.Intent == intent && a.ActionType == actionType);
                return planner.Score(candidate);
            }

            float plainAdvance = ScoreFor(false, EMacroIntent.EngageAtRange, EActionType.Advance);
            float cumbersomeAdvance = ScoreFor(true, EMacroIntent.EngageAtRange, EActionType.Advance);
            Assert.That(cumbersomeAdvance, Is.LessThan(plainAdvance - 0.01f),
                "the Advance-and-shoot volley is priced for an ordinary gun, never for a Cumbersome one.");

            Assert.That(ScoreFor(true, EMacroIntent.Hold, EActionType.Hold),
                Is.EqualTo(ScoreFor(false, EMacroIntent.Hold, EActionType.Hold)).Within(0.0001f),
                "Hold keeps the volley either way.");
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private sealed record World(TriggeredMoveTestContext Context, UnitActionContext UnitContext,
            DataBinding<UnitData> Unit);

        private static float MoveLength(ModelMoveEntry entry)
        {
            Position from = entry.Model.GetValue().Position;
            float total = 0f;
            foreach (Position to in entry.Positions)
            {
                total += Position.GetDistance2D(from, to);
                from = to;
            }
            return total;
        }

        // One 1-model unit (cannon + crew) at (10, 10) - never the origin, which is the unplaced sentinel - and one
        // enemy, both armies and teams registered.
        private World BuildWorld(bool cumbersome, Position enemyAt, IPlayerRequestByID? requester = null)
        {
            var player = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { player }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            DataBinding<UnitData> unit = MakeUnit(player, "Field Artillery", new Position(10f, 10f),
                new Weapon("Cannon", rangeInches: 36f, attacks: 1, armorPenetration: 2),
                new Weapon("Crew", rangeInches: 0f, attacks: 1, armorPenetration: 0));
            if (cumbersome)
                unit.GetValue().AttachRuleDefinition(new ResolvedRule("Cumbersome", CoreRuleCatalog.Cumbersome));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { unit }));

            DataBinding<UnitData> enemy = MakeUnit(enemyPlayer, "Grunts", enemyAt,
                new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemy }));

            var ctx = new TriggeredMoveTestContext(_store, requester ?? new CapturingStringSelectionRequester("Pass"));
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return new World(ctx, unitCtx, unit);
        }

        private static async Task<ChooseActionRequest> DriveChooseAction(World world)
        {
            var stage = new ChooseActionStage(world.Context, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCast.Bind("Cast");
            stage.ToCustomAction.Bind("Custom");
            stage.ToDisembark.Bind("Disembark");
            stage.ToEmbark.Bind("Embark");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(world.UnitContext);
            return ((CapturingStringSelectionRequester)world.Context.PlayerRequester).Captured!;
        }

        private static async Task RunMovement(World world)
        {
            var movement = new MovementStage(world.Context, new NoOpLayer<IUnitActionContext>());
            movement.BackToChooseAction.Bind("backToChooseAction");
            movement.OnFinishedMovement.Bind("finishedMovement");
            await movement.Enter(world.UnitContext);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position position, params Weapon[] weapons)
        {
            var model = new ModelData(0.5f, weapons.ToList(), position, _store);
            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings:
                new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
