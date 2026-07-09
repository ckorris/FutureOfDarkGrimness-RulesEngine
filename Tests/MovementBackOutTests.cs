using FDG.Data;
using FDG.Players;
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
}
