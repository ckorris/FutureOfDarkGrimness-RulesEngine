using System;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #035 slice D: mid-game embark, the rules-supplied way.
    //  - Eligibility (GetEmbarkableTransports): a friendly transport with room, on the table, within the
    //    unit's move-shoot distance — out-of-range / over-capacity transports are excluded.
    //  - ChooseActionStage: an "Embark" action is offered only when an engine spatial check finds an
    //    eligible transport (the availability gate is spatial, like Charge's), and choosing it routes to
    //    EmbarkStage.
    //  - EmbarkStage: boards the unit (EmbarkedIn token + models set aside off-table) and ENDS the
    //    activation (the unit is now inside); cancelling the transport choice returns to Choose Action.
    [TestFixture]
    public class TransportEmbarkTests
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
        public void GetEmbarkableTransports_FindsFriendlyTransportInRangeWithRoom()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, x: 12f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var eligible = EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue());

            Assert.That(eligible, Has.Count.EqualTo(1));
            Assert.That(eligible[0], Is.EqualTo(transport));
        }

        [Test]
        public void GetEmbarkableTransports_ExcludesOutOfRange()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeTransport("Rhino", capacity: 6, x: 60f, z: 10f); // far away
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue()), Is.Empty,
                "a transport beyond the unit's move distance can't be boarded this activation.");
        }

        [Test]
        public void GetEmbarkableTransports_ExcludesOverCapacity()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeTransport("Buggy", capacity: 1, x: 12f, z: 10f); // only 1 space
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f); // needs 2

            Assert.That(EmbarkStage.GetEmbarkableTransports(ctx, squad.GetValue()), Is.Empty);
        }

        [Test]
        public async Task ChooseAction_TransportInRange_OffersEmbark_AndRoutes()
        {
            var requester = new RecordingActionRequester("Embark");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            MakeTransport("Rhino", capacity: 6, x: 12f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);

            var unitCtx = NewActivation(ctx, squad);
            bool routedToEmbark = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToEmbark.OnWillActivate += _ => routedToEmbark = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Contain(CoreRuleCatalog.EmbarkRuleName),
                "Embark is offered when a friendly transport is in range.");
            Assert.That(routedToEmbark, Is.True, "choosing Embark routes to EmbarkStage.");
        }

        [Test]
        public async Task ChooseAction_NoTransport_DoesNotOfferEmbark()
        {
            var requester = new RecordingActionRequester(ChooseActionStage.PASS_CHOICE_NAME);
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f); // no transport anywhere

            var unitCtx = NewActivation(ctx, squad);
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToEmbark.Bind("ToEmbark");
            stage.ToReconcileEndOfActivation.Bind("end"); // the unit picks Pass, which routes here
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Does.Not.Contain(CoreRuleCatalog.EmbarkRuleName),
                "no transport in range → no Embark action.");
        }

        [Test]
        public async Task EmbarkStage_BoardsUnit_SetsAsideAndEndsActivation()
        {
            DataBinding<UnitData> transport = MakeTransport("Rhino", capacity: 6, x: 12f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);
            var ctx = new TriggeredMoveTestContext(_store, new EmbarkChoiceRequester(transport));

            var unitCtx = NewActivation(ctx, squad);
            bool embarked = false, back = false;
            var stage = new EmbarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnEmbarked.Bind("OnEmbarked"); stage.OnEmbarked.OnWillActivate += _ => embarked = true;
            stage.OnBackToChooseAction.Bind("OnBack"); stage.OnBackToChooseAction.OnWillActivate += _ => back = true;
            await stage.Enter(unitCtx);

            Assert.That(embarked, Is.True, "boarding fires OnEmbarked (ends the activation).");
            Assert.That(back, Is.False);
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.True);
            Assert.That(TransportUtilities.GetTransportId(squad.GetValue()), Is.EqualTo(transport.GetValue().ID));
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.False, "the unit is set aside off-table.");
        }

        [Test]
        public async Task EmbarkStage_Cancel_ReturnsToChooseAction()
        {
            MakeTransport("Rhino", capacity: 6, x: 12f, z: 10f);
            DataBinding<UnitData> squad = MakeSquad("Grunts", modelCount: 2, x: 10f, z: 10f);
            var ctx = new TriggeredMoveTestContext(_store, new EmbarkChoiceRequester(pickTransport: null)); // cancel

            var unitCtx = NewActivation(ctx, squad);
            bool embarked = false, back = false;
            var stage = new EmbarkStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnEmbarked.Bind("OnEmbarked"); stage.OnEmbarked.OnWillActivate += _ => embarked = true;
            stage.OnBackToChooseAction.Bind("OnBack"); stage.OnBackToChooseAction.OnWillActivate += _ => back = true;
            await stage.Enter(unitCtx);

            Assert.That(back, Is.True, "cancelling the transport choice returns to the action menu.");
            Assert.That(embarked, Is.False);
            Assert.That(TransportUtilities.IsEmbarked(squad.GetValue()), Is.False);
            Assert.That(squad.GetValue().GetIsOnBattlefield(), Is.True, "the unit stays on the table.");
        }

        // --- helpers ---

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        // A unit on the table carrying the universal Embark ability FDGServer attaches at army-load.
        private DataBinding<UnitData> MakeSquad(string name, int modelCount, float x, float z)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(
                new ResolvedRule(CoreRuleCatalog.EmbarkRuleName, CoreRuleCatalog.Embark));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeTransport(string name, int capacity, float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.AttachRuleDefinition(new ResolvedRule(TransportUtilities.TransportRuleName,
                CoreRuleCatalog.Transport, new RuleArgument[] { new RuleArgument.Int(capacity) }));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
