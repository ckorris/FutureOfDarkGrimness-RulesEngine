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
        public async Task DisembarkStage_PlacesUnembarksAndSpendsTheMove()
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
            Assert.That(unitCtx.HasMoved, Is.True, "exiting IS the unit's move action - it can't move again.");
            // #097: slice C recorded a flat 0 here, which let a unit whose Advance is shorter than the 6"
            // leash hop the full distance out and still shoot. The exit reports what it actually covered.
            Assert.That(unitCtx.MoveDistance, Is.EqualTo(2f).Within(0.001f),
                "the drop is 2\" from the transport, and that is the distance the exit spent.");
            Assert.That(finished, Is.True, "loops back to Choose Action.");
        }

        // #309: a networked client's renderer snapshots the unit's battlefield status from the
        // replicated state at the moment each model position lands (the position binding's
        // OnValueChanged - the same event ridden here). The EmbarkedIn clear must therefore
        // replicate BEFORE the exit positions, or the client captures a still-embarked unit and
        // renders the disembarked squad label-only until it next moves.
        [Test]
        public async Task DisembarkStage_UnembarksBeforeFirstPositionReplicates()
        {
            var requester = new CannedPlaceRequester(new Position(12f, 10f));
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var onBattlefieldAtEachUpdate = new List<bool>();
            foreach (DataBinding<ModelData> model in squad.GetValue().ModelBindings)
            {
                model.GetValue().PositionBinding.OnValueChanged +=
                    (_, _) => onBattlefieldAtEachUpdate.Add(squad.GetValue().GetIsOnBattlefield());
            }

            var unitCtx = NewActivation(ctx, squad);
            var stage = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(onBattlefieldAtEachUpdate, Is.Not.Empty, "the exit repositions the models");
            Assert.That(onBattlefieldAtEachUpdate, Is.All.True,
                "every replicated position update must already see the unit on the battlefield");
        }

        // #097: the distance is the FURTHEST model's, matching MovementUtilities.GetMaxMoveDistance's
        // max-over-models convention - one model lagging by the hull doesn't buy the squad a shot.
        [Test]
        public async Task DisembarkStage_RecordsTheFurthestModelsDrop()
        {
            var requester = new PerModelPlaceRequester(new Position(11f, 10f), new Position(15.5f, 10f));
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);
            var stage = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(unitCtx.MoveDistance, Is.EqualTo(5.5f).Within(0.001f),
                "1\" and 5.5\" drops from the transport - the exit cost is the 5.5\".");
        }

        // #097 pins what the Choose Action flow already allowed but nothing asserted: the exit spends the
        // MOVE, not the attack, so a unit that lands next to an enemy may charge straight out of the hatch.
        // (Charge is a separate menu action here, gated on melee range rather than on having moved.)
        [Test]
        public async Task Disembark_ThenCharge_IsOfferedWhenTheDropLandsInMeleeRange()
        {
            // Drop at (12,10); the enemy sits at (13,10), so the squad lands in base contact with it.
            var requester = new PlaceThenChooseRequester(new Position(12f, 10f),
                ChooseActionStage.CHARGE_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true);
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2,
                new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0));
            MakeEnemy("Cultists", x: 13f, z: 10f);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);
            var disembark = new DisembarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            disembark.OnFinished.Bind("OnFinished");
            await disembark.Enter(unitCtx);

            bool routedToCharge = false;
            var chooseAction = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            chooseAction.ToCharge.Bind("ToCharge");
            chooseAction.ToCharge.OnWillActivate += _ => routedToCharge = true;
            await chooseAction.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Contain(ChooseActionStage.CHARGE_CHOICE_NAME),
                "the 6\" exit put a model in melee range, so Charge is live.");
            Assert.That(requester.OfferedOptions, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "but the move is spent - the leash is the whole of the exit.");
            Assert.That(routedToCharge, Is.True, "choosing Charge routes to MeleeStage.");
        }

        // #097: an embarked unit's models sit at the origin, so snapshotting the charge-declaration
        // geometry off them measured from the table corner - garbage for every #197 "charges an enemy over
        // 9in away" rule the moment the unit disembarked and charged. It measures from the transport, which
        // is where the unit physically is when its activation begins.
        [Test]
        public void ActivationStart_EmbarkedUnit_MeasuresChargeOriginFromItsTransport()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, deployed: true); // (10,10)
            DataBinding<UnitData> squad = MakeSquadWithDisembark("Grunts", modelCount: 2);
            DataBinding<UnitData> enemy = MakeEnemy("Cultists", x: 30f, z: 10f);
            TransportUtilities.Embark(squad.GetValue(), transport.GetValue());

            var unitCtx = NewActivation(ctx, squad);

            Assert.That(unitCtx.TryGetActivationStartDistanceTo(enemy.GetValue().ID, out float distance),
                Is.True);
            Assert.That(distance, Is.EqualTo(19f).Within(0.01f),
                "20\" centre-to-centre from the transport, less the two 0.5\" bases.");
            Assert.That(distance, Is.LessThan(30f),
                "and emphatically not the ~30.6\" the at-origin models would have reported.");
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

        // An enemy unit (its own player, so it is never screened out as an ally) parked on the table.
        private DataBinding<UnitData> MakeEnemy(string name, float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var enemyPlayer = new PlayerID(Guid.NewGuid());
            var unit = new UnitData(enemyPlayer, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // A squad that carries the universal Disembark ability FDGServer attaches to every unit at army-load.
        private DataBinding<UnitData> MakeSquadWithDisembark(string name, int modelCount,
            params Weapon[] weapons)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(weapons), new Position(0f, 0f), _store);
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

        // #097: the greyed entries too, with their reasons — an option can be deliberately present-but-
        // unavailable (Embark's "move into contact first" hint), which OfferedOptions alone can't tell
        // apart from the option being absent entirely.
        public IReadOnlyList<StringSelectionRequest.InvalidOption> OfferedInvalidOptions { get; private set; }
            = new List<StringSelectionRequest.InvalidOption>();

        public RecordingActionRequester(string choice) => _choice = choice;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is StringSelectionRequest selection)
            {
                OfferedOptions = selection.ValidOptions;
                OfferedInvalidOptions = selection.InvalidOptions;
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

    // Answers a PlaceObjectsRequest by dealing the given positions out to the models in order (the last
    // one repeats if there are more models than positions), so a test can spread a squad's drop.
    internal sealed class PerModelPlaceRequester : IPlayerRequestByID
    {
        private readonly Position[] _positions;

        public PerModelPlaceRequester(params Position[] positions) => _positions = positions;

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData> place)
            {
                List<PlacedObjectEntry<ModelData>> placements = place.ModelsToPlace
                    .Select((binding, index) => new PlacedObjectEntry<ModelData>(
                        binding, _positions[Math.Min(index, _positions.Length - 1)]))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(placements));
            }
            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }

    // Answers a disembark placement with a fixed spot, then records and answers the action menu that
    // follows — so one context can drive DisembarkStage and the ChooseActionStage after it.
    internal sealed class PlaceThenChooseRequester : IPlayerRequestByID
    {
        private readonly Position _at;
        private readonly string _choice;

        public IReadOnlyList<string> OfferedOptions { get; private set; } = new List<string>();

        public PlaceThenChooseRequester(Position at, string choice)
        {
            _at = at;
            _choice = choice;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData> place)
            {
                List<PlacedObjectEntry<ModelData>> placements = place.ModelsToPlace
                    .Select(binding => new PlacedObjectEntry<ModelData>(binding, _at))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(placements));
            }

            if (request is StringSelectionRequest selection)
            {
                OfferedOptions = selection.ValidOptions;
                return Task.FromResult((TReply)(object)_choice);
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
