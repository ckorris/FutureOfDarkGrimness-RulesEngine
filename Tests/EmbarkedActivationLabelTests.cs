using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #315 vertical-slice integration test: the real ChooseUnitToActivateStage labels an embarked unit
    // with its transport — "Warriors (in Rhino)" — so two same-named units riding two transports are
    // distinguishable in the activation list (the 2026-08-01 game report). The suffix is engine-side so
    // the CLI, networked clients, and AI-visible labels all carry it; it applies to invalid options too
    // (an embarked unit that already activated still needs disambiguating from its twin). Mirrors
    // AmbushActivationReasonTests' harness.
    [TestFixture]
    public class EmbarkedActivationLabelTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task EmbarkedUnit_ValidOptionLabel_NamesItsTransport()
        {
            DataBinding<UnitData> transport = MakeUnit("Rhino", new Position(10f, 10f));
            DataBinding<UnitData> cargo = MakeUnit("Warriors", new Position(0f, 0f));
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { transport, cargo }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { transport, cargo });

            Assert.That(OptionName(request, cargo), Is.EqualTo("Warriors (in Rhino)"),
                "an embarked unit's label must say which transport it is riding in.");
        }

        [Test]
        public async Task TwoSameNamedUnits_InDifferentTransports_GetDistinctLabels()
        {
            DataBinding<UnitData> rhinoA = MakeUnit("Rhino A", new Position(10f, 10f));
            DataBinding<UnitData> rhinoB = MakeUnit("Rhino B", new Position(30f, 10f));
            DataBinding<UnitData> squadA = MakeUnit("Warriors", new Position(0f, 0f));
            DataBinding<UnitData> squadB = MakeUnit("Warriors", new Position(0f, 0f));
            TransportUtilities.Embark(squadA.GetValue(), rhinoA.GetValue());
            TransportUtilities.Embark(squadB.GetValue(), rhinoB.GetValue());
            _store.Create(new ArmyData(_player,
                new List<DataBinding<UnitData>> { rhinoA, rhinoB, squadA, squadB }));

            SelectionRequest<UnitData> request = await RunStage(
                unactivated: new[] { rhinoA, rhinoB, squadA, squadB });

            Assert.That(OptionName(request, squadA), Is.EqualTo("Warriors (in Rhino A)"));
            Assert.That(OptionName(request, squadB), Is.EqualTo("Warriors (in Rhino B)"),
                "the whole point: identically-named squads read differently through their rides.");
        }

        [Test]
        public async Task OnTableUnit_LabelStaysTheBareName()
        {
            DataBinding<UnitData> transport = MakeUnit("Rhino", new Position(10f, 10f));
            DataBinding<UnitData> walker = MakeUnit("Warriors", new Position(20f, 20f));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { transport, walker }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { transport, walker });

            Assert.That(OptionName(request, walker), Is.EqualTo("Warriors"),
                "a unit on the table gets no suffix — the hover ring already disambiguates it.");
            Assert.That(OptionName(request, transport), Is.EqualTo("Rhino"));
        }

        [Test]
        public async Task ActivatedEmbarkedUnit_InvalidOptionAlsoNamesTheTransport()
        {
            DataBinding<UnitData> transport = MakeUnit("Rhino", new Position(10f, 10f));
            DataBinding<UnitData> cargo = MakeUnit("Warriors", new Position(0f, 0f));
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { transport, cargo }));

            // Only the transport is left to activate: the cargo shows up greyed-out, but its label must
            // still carry the suffix so it reads apart from a same-named twin.
            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { transport });

            SelectionRequest<UnitData>.InvalidOption invalid =
                request.InvalidOptions.Single(o => o.Option == cargo);
            Assert.That(invalid.Name, Is.EqualTo("Warriors (in Rhino)"));
            Assert.That(invalid.Reason, Is.EqualTo("Already activated."));
        }

        private static string OptionName(SelectionRequest<UnitData> request, DataBinding<UnitData> unit) =>
            request.ValidOptions.Single(o => o.Option == unit).Name;

        private async Task<SelectionRequest<UnitData>> RunStage(
            IReadOnlyList<DataBinding<UnitData>> unactivated)
        {
            var requester = new CapturingSelectionRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToMainUnitAction.Bind("done");

            var turnContext = new SingleTurnContext(ctx, _player, unactivated.ToList());
            await stage.Enter(turnContext);

            Assert.That(requester.Captured, Is.Not.Null, "the stage issued a unit-selection request");
            return requester.Captured!;
        }

        private DataBinding<UnitData> MakeUnit(string name, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
