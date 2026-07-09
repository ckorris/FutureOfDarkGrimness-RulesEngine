using System;
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
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #035 slice C: disembark, the rules-supplied way.
    //  - Offer gate: the durable universal Disembark ability (AvailableWhen = TokenPresent(EmbarkedIn))
    //    is surfaced by GatherOffers only while the unit is embarked.
    //  - ChooseActionStage: an embarked unit is offered ONLY Disembark (+ Pass) — not Move/Shoot/Charge,
    //    which would otherwise act on its at-origin models — and choosing it routes to DisembarkStage.
    //  - DisembarkStage: places the unit within 6" of its transport, un-embarks it (clears the token, so
    //    it's now on the battlefield), and registers the move as an Advance (HasMoved, MoveDistance 0).
    [TestFixture]
    public class TransportDisembarkTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void Disembark_OfferedOnlyWhileEmbarked()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);

            Assert.That(ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(squad.GetValue())), Is.Empty,
                "a unit that isn't embarked is not offered Disembark.");

            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(squad.GetValue()));
            Assert.That(offers.Count, Is.EqualTo(1));
            Assert.That(offers[0].RuleName, Is.EqualTo(CoreRuleCatalog.DisembarkRuleName));
        }

        [Test]
        public async Task ChooseAction_EmbarkedUnit_OffersOnlyDisembark_AndRoutes()
        {
            var requester = new RecordingActionRequester("Disembark");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);
            bool routedToDisembark = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToDisembark.Bind("ToDisembark");
            stage.ToDisembark.OnWillActivate += _ => routedToDisembark = true;
            await stage.Enter(unitCtx);

            Assert.That(routedToDisembark, Is.True, "choosing Disembark routes to DisembarkStage.");
            Assert.That(requester.OfferedOptions, Does.Contain("Disembark"));
            Assert.That(requester.OfferedOptions, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "an embarked unit can't Move its at-origin models — only Disembark (or Pass).");
        }

        [Test]
        public async Task DisembarkStage_PlacesUnembarksAndCountsAsAdvance()
        {
            var requester = new CannedPlaceRequester(new Position(12f, 10f)); // within 6" of the transport at (10,10)
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);
            bool finished = false;
            var stage = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.False, "the unit is no longer embarked.");
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.True, "its models are now on the table.");
            Assert.That(unitCtx.HasMoved, Is.True, "disembarking is the unit's move (Advance-equivalent).");
            Assert.That(unitCtx.MoveDistance, Is.EqualTo(0f), "an Advance with 0 measured distance keeps shooting available.");
            Assert.That(finished, Is.True, "loops back to Choose Action.");
        }

        // Nothing is repositioned until the placements come back, so cancelling the prompt must leave the
        // unit aboard with its move unspent — the same back-out Embark has always had.
        [Test]
        public async Task DisembarkStage_PlayerCancelsPlacement_StaysAboardWithMoveUnspent()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CancellingPlaceRequester());
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);
            bool finished = false, backedOut = false;
            var stage = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnBackToChooseAction.Bind("OnBackToChooseAction");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            stage.OnBackToChooseAction.OnWillActivate += _ => backedOut = true;
            await stage.Enter(unitCtx);

            Assert.That(backedOut, Is.True, "cancelling routes to the back-out exit.");
            Assert.That(finished, Is.False, "the back-out must not travel the finished exit.");
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True, "the unit is still aboard.");
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.False, "no model was repositioned.");
            Assert.That(unitCtx.HasMoved, Is.False, "a disembark that never happened doesn't spend the move.");
        }

        [Test]
        public async Task DisembarkStage_PlacementRequest_OffersCancel()
        {
            var requester = new CannedPlaceRequester(new Position(12f, 10f));
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var stage = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnBackToChooseAction.Bind("OnBackToChooseAction");
            await stage.Enter(NewActivation(ctx, squad));

            Assert.That(requester.LastRequest, Is.Not.Null);
            Assert.That(requester.LastRequest!.AllowCancel, Is.True,
                "the disembark placement offers a Back button; deployment and spillout do not.");
        }

        // --- helpers ---

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        // A squad that carries the universal Disembark ability FDGServer attaches to every unit at army-load.
        private DataBinding<UnitData> MakeSquadWithDisembark(string name, int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(
                new ResolvedRule(CoreRuleCatalog.DisembarkRuleName, CoreRuleCatalog.Disembark));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, bool deployed)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (deployed) modelBinding.GetValue().SetPosition(new Position(10f, 10f));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Records the offered action options (the valid choices) and answers with a fixed choice.
    internal sealed class RecordingActionRequester : IPlayerRequestByID
    {
        private readonly string _choice;
        public IReadOnlyList<string> OfferedOptions { get; private set; } = new List<string>();

        public RecordingActionRequester(string choice) => _choice = choice;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest selection)
            {
                OfferedOptions = selection.ValidOptions;
                return Task.FromResult((TReply)(object)_choice);
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Answers a PlaceObjectsRequest by putting every model at a fixed position.
    internal sealed class CannedPlaceRequester : IPlayerRequestByID
    {
        private readonly Position _at;

        public CannedPlaceRequester(Position at) => _at = at;

        public PlaceObjectsRequest<ModelData>? LastRequest { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData> place)
            {
                LastRequest = place;
                List<PlacedObjectEntry<ModelData>> placements = place.ModelsToPlace
                    .Select(binding => new PlacedObjectEntry<ModelData>(binding, _at))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(placements));
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Backs out of the placement prompt, the way a player clicking Back does.
    internal sealed class CancellingPlaceRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData>)
                return Task.FromResult((TReply)(object)new Cancelled<List<PlacedObjectEntry<ModelData>>>());
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
