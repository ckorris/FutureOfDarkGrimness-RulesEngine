using System;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #029 — the Aircraft forced-movement helper: heading init (toward the table centre, set once and never
    // turned), rigid straight-line paths, and off-table detection.
    [TestFixture]
    public class ForcedAircraftMoveTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void GetHeading_ReadsTheSharedDeployFacing_NoAutoAim()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            // The heading IS the facing the player deployed with (#150): default +Z until rotated…
            Assert.That(ForcedAircraftMove.GetHeading(unit.GetValue()), Is.EqualTo(new Float2(0f, 1f)),
                "un-rotated deploy facing (+Z) is the heading — nothing auto-aims it elsewhere.");

            // …and exactly whatever the player set, thereafter.
            unit.GetValue().Models[0].SetFacing(new Float2(1f, 0f));
            Assert.That(ForcedAircraftMove.GetHeading(unit.GetValue()), Is.EqualTo(new Float2(1f, 0f)),
                "the heading follows the placed facing.");
        }

        [Test]
        public void GetHeading_DivergentModelFacings_Throws()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10), new Position(12, 10));
            unit.GetValue().Models[0].SetFacing(new Float2(0f, 1f));
            unit.GetValue().Models[1].SetFacing(new Float2(1f, 0f));

            Assert.Throws<InvalidOperationException>(() => ForcedAircraftMove.GetHeading(unit.GetValue()),
                "an Aircraft's models must share one heading — divergence is a bug, not a choice.");
        }

        [Test]
        public void BuildPaths_TranslatesEveryModelByHeadingTimesDistance()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            System.Collections.Generic.List<ModelMoveEntry> paths =
                ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 30f);

            Assert.That(paths.Count, Is.EqualTo(1));
            Position dest = paths[0].Positions[0];
            Assert.That(dest.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(dest.z, Is.EqualTo(40f).Within(0.001f), "moved 30\" straight along +z.");
        }

        [Test]
        public void WouldLeaveTable_FalseWhenInBounds_TrueOverTheEdge()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(10, 10));

            var inBounds = ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 30f);  // -> (10,40); table H=48
            Assert.That(ForcedAircraftMove.WouldLeaveTable(inBounds), Is.False);

            var offEdge = ForcedAircraftMove.BuildPaths(unit, new Float2(0f, 1f), 40f);   // -> (10,50) past the 48 edge
            Assert.That(ForcedAircraftMove.WouldLeaveTable(offEdge), Is.True);
        }

        // Integration: drive DefinePathStage with an Aircraft and confirm the forced move's two outcomes.
        // (OnPathDefined is dropped by the NoOpLayer, so the on-table move is SUBMITTED but its downstream commit
        // doesn't run — the off-table branch, which mutates models directly, is the one fully observable here.)
        [Test]
        public async Task AircraftAdvance_OffTableEdge_LeavesPlayAndMarksForRedeploy()
        {
            var ctx = new TriggeredMoveTestContext(_store, new FirstStringRequester());

            DataBinding<UnitData> aircraft = MakeUnit(new Position(10, 40)); // near the top edge (H = 48)
            aircraft.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            // Facing +z (straight off the top edge) — the deploy facing IS the heading.
            aircraft.GetValue().Models[0].SetFacing(new Float2(0f, 1f));

            var moveCtx = new MovementActionContext(ctx, aircraft);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveCtx);

            Position p = aircraft.GetValue().Models[0].Position;
            Assert.That(p.x == 0f && p.z == 0f, Is.True, "flew off the table — held off-table (models at origin).");
            Assert.That(aircraft.GetValue().Tokens.HasToken(TokenType.OffTableFromForcedMove), Is.True,
                "marked for redeployment next round.");
        }

        [Test]
        public async Task AircraftAdvance_StayingOnTable_DoesNotLeavePlay()
        {
            var ctx = new TriggeredMoveTestContext(_store, new FirstStringRequester());

            DataBinding<UnitData> aircraft = MakeUnit(new Position(10, 5)); // default deploy facing +z; 30" → (10,35), on-table
            aircraft.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));

            var moveCtx = new MovementActionContext(ctx, aircraft);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveCtx);

            Assert.That(aircraft.GetValue().Tokens.HasToken(TokenType.OffTableFromForcedMove), Is.False,
                "an Aircraft that stays on the table is not removed from play.");
            Assert.That(moveCtx.TryGetPaths(out var paths), Is.True);
            Position dest = paths[0].Positions[0];
            Assert.That(dest.x != 10f || dest.z != 5f, Is.True, "a forced straight-line move was submitted.");
        }

        [Test]
        public async Task AircraftAdvance_UsesTheResolverChosenDistance()
        {
            // #029: the forced move is a continuous 30–36" band now (not 30/33/36) — the stage must fly exactly
            // the distance the resolver picked.
            var ctx = new TriggeredMoveTestContext(_store, new FixedAircraftDistanceRequester(33.7f));
            DataBinding<UnitData> aircraft = MakeUnit(new Position(10, 5)); // facing +z; 33.7" → (10, 38.7), on-table
            aircraft.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));

            var moveCtx = new MovementActionContext(ctx, aircraft);
            var stage = new DefinePathStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnPathDefined.Bind("done");
            await stage.Enter(moveCtx);

            Assert.That(moveCtx.TryGetPaths(out var paths), Is.True);
            Position dest = paths[0].Positions[0];
            Assert.That(dest.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(dest.z, Is.EqualTo(38.7f).Within(0.001f), "flies exactly the chosen distance along its heading.");
        }

        // #029 — an Aircraft is FORCED to Advance every activation, and shooting only happens after moving
        // ("you can't move after you shoot"). So on a fresh activation Move is the ONLY offered action: Shoot
        // and Pass are gated until the forced Advance is done. The two tests below drive ChooseActionStage
        // before and after the move over the same world (Aircraft with a rifle, an enemy sitting in range).

        [Test]
        public async Task ChooseAction_PristineAircraft_OffersOnlyMove_ShootAndPassGatedUntilItMoves()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            var (ctx, unitCtx) = BuildAircraftActionWorld(capturer);

            await DriveChooseAction(ctx, unitCtx);

            ChooseActionRequest req = capturer.Captured!;
            Assert.That(req.ValidOptions, Is.EquivalentTo(new[] { ChooseActionStage.MOVEMENT_CHOICE_NAME }),
                "a pristine Aircraft must be offered Move and nothing else - it has to Advance first.");
            Assert.That(req.InvalidOptions.Any(o =>
                    o.Option == ChooseActionStage.SHOOT_CHOICE_NAME && o.Reason.Contains("Advance")),
                Is.True, "Shoot is shown disabled until the Aircraft has moved, even with an enemy in range.");
            Assert.That(req.InvalidOptions.Any(o =>
                    o.Option == ChooseActionStage.PASS_CHOICE_NAME && o.Reason.Contains("Advance")),
                Is.True, "Pass is shown disabled until the Aircraft has moved.");
        }

        [Test]
        public async Task ChooseAction_AircraftAfterMoving_UnlocksShootAndPass()
        {
            var capturer = new CapturingStringSelectionRequester(ChooseActionStage.SHOOT_CHOICE_NAME);
            var (ctx, unitCtx) = BuildAircraftActionWorld(capturer);
            unitCtx.RegisterMoveFinished(ForcedAircraftMove.MinDistanceInches, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES); // the forced Advance completed

            await DriveChooseAction(ctx, unitCtx);

            ChooseActionRequest req = capturer.Captured!;
            Assert.That(req.ValidOptions, Does.Contain(ChooseActionStage.SHOOT_CHOICE_NAME),
                "once the Aircraft has Advanced it may shoot (enemy is in range).");
            Assert.That(req.ValidOptions, Does.Contain(ChooseActionStage.PASS_CHOICE_NAME),
                "once the Aircraft has Advanced it may also decline to shoot and Pass.");
            Assert.That(req.ValidOptions, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "it has already moved, so Move is spent.");
        }

        // Direct, LoS-independent statement of the Pass gate (GetCanPass is public).
        [Test]
        public void GetCanPass_Aircraft_BlockedUntilMoved_ThenAllowed()
        {
            var (ctx, unitCtx) = BuildAircraftActionWorld(new FirstStringRequester(), enemyInRange: false);

            Assert.That(ChooseActionStage.GetCanPass(ctx, unitCtx, out string reason), Is.False,
                "a pristine Aircraft cannot Pass - it must Advance.");
            Assert.That(reason, Does.Contain("Advance"));

            unitCtx.RegisterMoveFinished(ForcedAircraftMove.MinDistanceInches, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);
            Assert.That(ChooseActionStage.GetCanPass(ctx, unitCtx, out _), Is.True,
                "after completing its forced Advance the Aircraft may Pass.");
        }

        private static async Task DriveChooseAction(TriggeredMoveTestContext ctx, UnitActionContext unitCtx)
        {
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToMovement.Bind("Move");
            stage.ToCharge.Bind("Charge");
            stage.ToShoot.Bind("Shoot");
            stage.ToCast.Bind("Cast");
            stage.ToReconcileEndOfActivation.Bind("Done");
            await stage.Enter(unitCtx);
        }

        // A one-Aircraft-vs-one-enemy world: teams (needed by the shoot target check), the Aircraft armed with a
        // 24" rifle, and (by default) an enemy 10" away in clear line of sight. The context is built with the
        // supplied requester so the caller can capture the ChooseAction menu.
        private (TriggeredMoveTestContext ctx, UnitActionContext unitCtx) BuildAircraftActionWorld(
            IPlayerRequestByID requester, bool enemyInRange = true)
        {
            var aircraftPlayer = new PlayerID(Guid.NewGuid());
            var enemyPlayer = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { aircraftPlayer }));
            _store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var aircraftModel = new ModelData(0.75f, new List<Weapon> { rifle }, new Position(10, 10), _store);
            var aircraft = new UnitData(aircraftPlayer, "Jet", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                { _store.GetDataBinding<ModelData>(_store.Create(aircraftModel)) });
            DataBinding<UnitData> aircraftBinding = _store.GetDataBinding<UnitData>(_store.Create(aircraft));
            aircraftBinding.GetValue().AttachRuleDefinition(new ResolvedRule("Aircraft", CoreRuleCatalog.Aircraft));
            _store.Create(new ArmyData(aircraftPlayer, new List<DataBinding<UnitData>> { aircraftBinding }));

            if (enemyInRange)
            {
                var enemyModel = new ModelData(0.75f, new List<Weapon>(), new Position(10, 20), _store); // 10" away
                var enemy = new UnitData(enemyPlayer, "Ground Troop", quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>>
                    { _store.GetDataBinding<ModelData>(_store.Create(enemyModel)) });
                DataBinding<UnitData> enemyBinding = _store.GetDataBinding<UnitData>(_store.Create(enemy));
                _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyBinding }));
            }

            var ctx = new TriggeredMoveTestContext(_store, requester);
            var unitCtx = new UnitActionContext(ctx, aircraftBinding);
            unitCtx.Reset(aircraftBinding);
            return (ctx, unitCtx);
        }

        private DataBinding<UnitData> MakeUnit(params Position[] positions)
        {
            var bindings = new List<DataBinding<ModelData>>();
            foreach (Position pos in positions)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), pos, _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Jet", quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    // Answers the Aircraft forced-move with a fixed chosen distance (stay-on intent; the stage's geometry
    // check is authoritative either way).
    internal sealed class FixedAircraftDistanceRequester : IPlayerRequestByID
    {
        private readonly float _distance;
        public FixedAircraftDistanceRequester(float distance) => _distance = distance;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is AircraftAdvanceRequest)
                return Task.FromResult((TReply)(object)new AircraftAdvanceResult(_distance, false));
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Answers the Aircraft forced-move with its minimum distance (staying on the table where geometry allows;
    // the stage's WouldLeaveTable check is authoritative either way), and any StringSelectionRequest with its
    // first valid option.
    internal sealed class FirstStringRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is AircraftAdvanceRequest a)
                return Task.FromResult((TReply)(object)new AircraftAdvanceResult(a.MinDistanceInches, false));
            if (request is StringSelectionRequest s)
                return Task.FromResult((TReply)(object)s.ValidOptions[0]);
            if (request is ChooseActionRequest ca)
                return Task.FromResult((TReply)(object)ca.ValidOptions[0]);
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
