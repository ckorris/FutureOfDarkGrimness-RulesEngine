using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #248: backing out of a pristine activation. ChooseActionStage offers a cancellable action menu
    // (StringSelectionRequest.AllowCancel) only while NOTHING irreversible has happened - not moved, not
    // attacked, no activation-start rule applied, no tokens spent. A null reply (the GUI Back button /
    // Esc, or the CLI's [0]) routes out through ChooseActionStage.ToBackOut -> MainUnitActionStage's own
    // OnBackedOut sibling exit, so nothing marks the unit as activated and SingleTurnStage returns to
    // unit selection. The cancel is deliberately NOT a listed option: AI resolvers and the CLI EOF
    // default only ever pick real options, so an automated player can never loop the turn.
    //
    // Mirrors MeleeBackOutTests: drives the real stages so the wiring itself is under test.
    [TestFixture]
    public class ActivationBackOutTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;
        private ActionMenuRequester _requester = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new ActionMenuRequester();
            _ctx = new WoundTestContext(_store, _requester);
        }

        [Test]
        public async Task PristineActivation_MenuIsCancellable_AndCancelBacksOut()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(5f, 0f, 0f));
            _requester.ReplyCancel = true;

            var (context, backedOut, toMovement, reconciled) = await RunChooseAction(unit);

            Assert.That(_requester.LastActionRequest, Is.Not.Null);
            Assert.That(_requester.LastActionRequest!.AllowCancel, Is.True,
                "an untouched activation's action menu must be cancellable.");
            Assert.That(backedOut, Is.True, "a cancel reply routes to the back-out exit.");
            Assert.That(toMovement, Is.False);
            Assert.That(reconciled, Is.False, "backing out must not travel the end-of-activation exit.");
            Assert.That(context.HasMoved, Is.False);
            Assert.That(context.HasAttacked, Is.False);
        }

        [Test]
        public async Task AfterMoving_MenuIsNotCancellable()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(5f, 0f, 0f));   // in rifle range, so Shoot keeps the menu alive post-move
            _requester.ReplyOption = ChooseActionStage.PASS_CHOICE_NAME;

            var (_, backedOut, _, reconciled) = await RunChooseAction(unit,
                context => context.RegisterMoveFinished(1f));

            Assert.That(_requester.LastActionRequest, Is.Not.Null);
            Assert.That(_requester.LastActionRequest!.AllowCancel, Is.False,
                "once the unit has moved there is no clean way back to unit selection.");
            Assert.That(backedOut, Is.False);
            Assert.That(reconciled, Is.True, "Pass ends the activation normally.");
        }

        [Test]
        public async Task AfterIrreversibleAction_MenuIsNotCancellable()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(5f, 0f, 0f));

            var (_, backedOut, toMovement, _) = await RunChooseAction(unit,
                context => context.MarkIrreversibleAction());

            Assert.That(_requester.LastActionRequest, Is.Not.Null);
            Assert.That(_requester.LastActionRequest!.AllowCancel, Is.False,
                "an activation-start effect / token spend closes the back-out window.");
            Assert.That(backedOut, Is.False);
            Assert.That(toMovement, Is.True, "the default reply (first option) is Move.");
        }

        [Test]
        public void CancelReply_WhenMenuNotCancellable_Throws()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(5f, 0f, 0f));
            _requester.ReplyCancel = true;

            Assert.ThrowsAsync<System.ArgumentException>(async () =>
                await RunChooseAction(unit, context => context.MarkIrreversibleAction()),
                "a rogue cancel on a mandatory menu must fail loudly, not silently un-activate.");
        }

        // The wiring above ChooseActionStage: the cancel travels MainUnitActionStage's own OnBackedOut
        // sibling (built in PopulateTransitions), NOT the end-of-activation reconcile - so nothing marks
        // the unit activated. Drives the full pipeline: ActivationStartStage (no rules -> no ops -> still
        // pristine) then the cancellable Choose Action.
        [Test]
        public async Task MainUnitActionStage_BackOut_LeavesThroughItsOwnExit()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemyArmy(new Position(5f, 0f, 0f));
            _requester.ReplyCancel = true;

            var turnContext = new SingleTurnContext(_ctx, unit.GetValue().PlayerID,
                new List<DataBinding<UnitData>> { unit });
            turnContext.ChooseUnitToActivate(unit);

            var main = new MainUnitActionStage(_ctx, new NoOpLayer<ISingleTurnContext>());
            bool backedOut = false, reconciled = false;
            main.OnBackedOut.Bind("backedOut");
            main.ToReconcileEndOfActivation.Bind("reconcile");
            main.OnBackedOut.OnWillActivate += _ => backedOut = true;
            main.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;

            await main.Enter(turnContext);

            Assert.That(backedOut, Is.True, "the back-out must leave through OnBackedOut.");
            Assert.That(reconciled, Is.False,
                "the back-out must not travel the exit that marks the unit activated.");
            Assert.That(turnContext.WasDelayed, Is.False);
        }

        private async Task<(UnitActionContext Context, bool BackedOut, bool ToMovement, bool Reconciled)>
            RunChooseAction(DataBinding<UnitData> unit, System.Action<UnitActionContext>? mutate = null)
        {
            var context = new UnitActionContext(_ctx, unit);
            context.Reset(unit);
            mutate?.Invoke(context);

            var stage = new ChooseActionStage(_ctx, new NoOpLayer<IUnitActionContext>());
            bool backedOut = false, toMovement = false, reconciled = false;
            stage.ToBackOut.Bind("backOut");
            stage.ToMovement.Bind("toMovement");
            stage.ToShoot.Bind("toShoot");
            stage.ToReconcileEndOfActivation.Bind("reconcile");
            stage.ToBackOut.OnWillActivate += _ => backedOut = true;
            stage.ToMovement.OnWillActivate += _ => toMovement = true;
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;

            await stage.Enter(context);
            return (context, backedOut, toMovement, reconciled);
        }

        private int _nextTeamIndex;

        private void MakeEnemyArmy(params Position[] modelPositions)
        {
            PlayerID enemyPlayer = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> enemyUnit = MakeUnit(enemyPlayer, modelPositions);
            ArmyData army = new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { enemyUnit });
            _store.Create(army);
        }

        private DataBinding<UnitData> MakeUnit(params Position[] modelPositions)
            => MakeUnit(new PlayerID(System.Guid.NewGuid()), modelPositions);

        // A blade (melee) + rifle (ranged) so the action menu has real choices: Move + Shoot when an
        // enemy sits in range, Charge grayed out past 2". Every player gets their own team — the
        // shoot gate resolves the attacker's team to screen out allies.
        private DataBinding<UnitData> MakeUnit(PlayerID playerID, params Position[] modelPositions)
        {
            _store.Create(new TeamData(_nextTeamIndex++, new List<PlayerID> { playerID }));

            var modelBindings = new List<DataBinding<ModelData>>(modelPositions.Length);
            foreach (Position position in modelPositions)
            {
                var weapons = new List<Weapon>
                {
                    new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0),
                    new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0),
                };
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: weapons,
                    initialPosition: position,
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Scripted action-menu answers: captures the request (for AllowCancel assertions) and replies
        // with a cancel (null), a named option, or the first valid option.
        internal sealed class ActionMenuRequester : IPlayerRequestByID
        {
            public StringSelectionRequest? LastActionRequest { get; private set; }
            public bool ReplyCancel { get; set; }
            public string? ReplyOption { get; set; }

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest menu)
                {
                    LastActionRequest = menu;
                    string? reply = ReplyCancel ? null : (ReplyOption ?? menu.ValidOptions[0]);
                    return Task.FromResult((TReply)(object)reply!);
                }
                throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
            }
        }
    }
}
