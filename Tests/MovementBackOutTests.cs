using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Abandoning a Move at the path prompt must not spend the unit's move.
    //
    // DefineMovementPathRequest is cancellable (AllowCancel) and DefinePathStage routes a Cancelled reply
    // to MovementStage's own BackToChooseAction sibling. Nothing has been mutated at that point: positions
    // are written by ExecuteMoveStage and strafing hits are rolled after the path is submitted.
    //
    // MovementStage.ReconcileChildContextBeforeLeaving runs on EVERY sibling exit and throws when no path
    // was submitted, so the back-out also needs the MoveCancelled guard — these tests cover both halves.
    [TestFixture]
    public class MovementBackOutTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task PlayerCancelsPathPrompt_BacksOut_WithoutSpendingTheMove()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CancellingMoveRequester(), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));
            Position before = unit.GetValue().ModelBindings[0].GetValue().Position;

            var context = new UnitActionContext(ctx, unit);
            var movement = new MovementStage(ctx, new NoOpLayer<IUnitActionContext>());

            bool backedOut = false, finishedMovement = false;
            movement.BackToChooseAction.Bind("backToChooseAction");
            movement.OnFinishedMovement.Bind("finishedMovement");
            movement.BackToChooseAction.OnWillActivate += _ => backedOut = true;
            movement.OnFinishedMovement.OnWillActivate += _ => finishedMovement = true;

            await movement.Enter(context);

            Assert.That(backedOut, Is.True, "a cancelled path prompt routes to the back-out exit.");
            Assert.That(finishedMovement, Is.False, "the back-out must not travel the movement-finished exit.");
            Assert.That(context.HasMoved, Is.False,
                "a move that never happened must leave the unit free to choose another action.");

            Position after = unit.GetValue().ModelBindings[0].GetValue().Position;
            Assert.That(after.x, Is.EqualTo(before.x).Within(0.001f), "nothing moved.");
            Assert.That(after.z, Is.EqualTo(before.z).Within(0.001f), "nothing moved.");
        }

        [Test]
        public void MoveActionRequest_OffersCancel()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CapturingMoveRequester(), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));

            var movement = new MovementStage(ctx, new NoOpLayer<IUnitActionContext>());
            movement.BackToChooseAction.Bind("backToChooseAction");
            movement.OnFinishedMovement.Bind("finishedMovement");

            Assert.ThrowsAsync<CancelledMoveSentinel>(async () =>
                await movement.Enter(new UnitActionContext(ctx, unit)));

            var requester = (CapturingMoveRequester)ctx.PlayerRequester;
            Assert.That(requester.Captured, Is.Not.Null);
            Assert.That(requester.Captured!.AllowCancel, Is.True,
                "the player-chosen Move action offers a Back button; a rule-triggered move does not.");
        }

        // ── #197 Mobile Artillery: the round-scoped MovedThisRound stamp ────────────────────────────────

        // The rule's defensive arm is Not(TokenPresent(MovedThisRound)), read during the ENEMY's activation,
        // so the fact has to be stamped on the UNIT when a move resolves - not inferred from the mover's own
        // per-activation context, which nothing outside that activation can see. Driven through the REAL
        // MovementStage so the seam under test is the production reconcile, not a hand-rolled equivalent.
        [Test]
        public async Task ResolvedMove_StampsMovedThisRound()
        {
            var ctx = new TriggeredMoveTestContext(_store, new ResolvingMoveRequester(3f), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.False,
                "precondition: a unit that has not moved carries nothing.");

            await RunMovement(ctx, unit);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.True,
                "a resolved move must be legible to a rule reading the unit from the other side of the table.");
        }

        // The back-out shares this seam (the reconcile runs on every exit), so a cancelled move must not
        // stamp - otherwise mis-clicking Move would silently switch off an artillery piece's defensive bonus
        // for the whole round without it having moved an inch.
        [Test]
        public async Task CancelledMove_DoesNotStampMovedThisRound()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CancellingMoveRequester(), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));

            await RunMovement(ctx, unit);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.False,
                "a move that never happened leaves the unit as if it had held.");
        }

        // #333: the third way out of MovementStage - a move that RESOLVED but covered no ground. "Skip all"
        // in the GUI (and a Done with no waypoints placed) submits a real path whose every waypoint sits on
        // the model's own position, so it travels OnFinishedMovement, not the back-out, and used to stamp.
        // The unit has genuinely not moved, so the round-scoped fact must read that way; the per-activation
        // Move action is still spent, which is the half a player DOES pay for finishing an empty move.
        [Test]
        public async Task ZeroDistanceMove_SpendsTheMove_ButDoesNotStampMovedThisRound()
        {
            var ctx = new TriggeredMoveTestContext(_store, new ResolvingMoveRequester(0f), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));

            var context = new UnitActionContext(ctx, unit);
            var movement = new MovementStage(ctx, new NoOpLayer<IUnitActionContext>());
            movement.BackToChooseAction.Bind("backToChooseAction");
            movement.OnFinishedMovement.Bind("finishedMovement");
            await movement.Enter(context);

            Assert.That(context.HasMoved, Is.True,
                "finishing an empty move still spends the unit's Move action - Back is what keeps it.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.False,
                "a unit that travelled 0in has not moved this round, whichever exit it left through.");
        }

        // The guard is an epsilon against float drift, not a "barely moved" allowance: a real move of any
        // size still stamps. Pinned so nobody widens it into a rule.
        [Test]
        public async Task TinyButRealMove_StillStampsMovedThisRound()
        {
            var ctx = new TriggeredMoveTestContext(_store, new ResolvingMoveRequester(0.25f), new FixedDiceRoller(4));
            DataBinding<UnitData> unit = MakeUnit(ctx, new Position(10f, 10f));

            await RunMovement(ctx, unit);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.MovedThisRound), Is.True,
                "0.25in is a move; only a genuinely zero-length path is exempt.");
        }

        // The token is round-scoped, not game-scoped: it must come off in the round-end sweep, or an
        // artillery piece that moved once would never get its bonus back.
        [Test]
        public void MovedThisRound_ClearsAtRoundEnd()
        {
            Assert.That(TokenDefinitionCatalog.Lookup(TokenType.MovedThisRound).DefaultClearTrigger,
                Is.InstanceOf<TokenClearTrigger.RoundEnd>(),
                "'hasn't moved during the ROUND' - the window reopens next round.");
        }

        private static async Task RunMovement(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var movement = new MovementStage(ctx, new NoOpLayer<IUnitActionContext>());
            movement.BackToChooseAction.Bind("backToChooseAction");
            movement.OnFinishedMovement.Bind("finishedMovement");
            await movement.Enter(new UnitActionContext(ctx, unit));
        }

        private DataBinding<UnitData> MakeUnit(IGameContext ctx, Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Mover", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }

    internal sealed class CancellingMoveRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest)
                return Task.FromResult((TReply)(object)new Cancelled<List<ModelMoveEntry>>());
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Captures the request then aborts, so the test can inspect AllowCancel without resolving a whole move.
    internal sealed class CapturingMoveRequester : IPlayerRequestByID
    {
        public DefineMovementPathRequest? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest moveRequest)
            {
                Captured = moveRequest;
                throw new CancelledMoveSentinel();
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    internal sealed class CancelledMoveSentinel : System.Exception { }

    // Answers the path prompt with a straight move of the given length for every model, so the whole
    // MovementStage chain resolves and its reconcile runs.
    internal sealed class ResolvingMoveRequester : IPlayerRequestByID
    {
        private readonly float _distanceInches;
        public ResolvingMoveRequester(float distanceInches) => _distanceInches = distanceInches;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest move)
            {
                List<ModelMoveEntry> entries = move.UnitDataBinding.GetValue().ModelBindings
                    .Where(m => m.GetValue().GetIsAlive())
                    .Select(m => new ModelMoveEntry(m, new List<Position>
                    {
                        new Position(m.GetValue().Position.x + _distanceInches, m.GetValue().Position.z),
                    }))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
