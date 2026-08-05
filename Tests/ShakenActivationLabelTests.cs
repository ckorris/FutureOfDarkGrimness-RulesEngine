using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #337 vertical-slice integration test: the real ChooseUnitToActivateStage badges a Shaken unit in the
    // activation list — "Blade Squad (Shaken - recovers)". Activating one skips the action menu entirely
    // (ChooseActionStage sees StartedActivationShaken and spends the whole activation recovering), so the
    // picker used to offer a unit that could not act with nothing to say about it: the only warning was a
    // Toast that had already faded, and a Shaken unit standing inside the 1" forced-charge band silently
    // declined to charge, which reads as the proximity rule being broken (2026-08-04 playtest).
    //
    // Engine-side like the #315 transport suffix it stacks with, so the CLI picker, networked clients and
    // AI-visible labels all carry it; the GUI additionally re-draws the badge run amber and hoverable.
    // Mirrors EmbarkedActivationLabelTests' harness.
    [TestFixture]
    public class ShakenActivationLabelTests
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
        public async Task ShakenUnit_ValidOptionLabel_CarriesTheBadge()
        {
            DataBinding<UnitData> shaken = MakeUnit("Blade Squad", new Position(20f, 20f));
            Shake(shaken);
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { shaken }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { shaken });

            Assert.That(OptionName(request, shaken), Is.EqualTo("Blade Squad (Shaken - recovers)"),
                "a Shaken unit's row must say so - activating it forfeits the activation.");
        }

        [Test]
        public async Task UnshakenUnit_LabelStaysTheBareName()
        {
            DataBinding<UnitData> steady = MakeUnit("Gun Squad", new Position(20f, 20f));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { steady }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { steady });

            Assert.That(OptionName(request, steady), Is.EqualTo("Gun Squad"),
                "no badge on a unit that can act normally - the list must stay quiet by default.");
        }

        // The two suffixes stack in a fixed order: the transport names WHICH unit this is, the status says
        // what activating it will do. Pinned because the GUI locates the badge by searching the finished
        // label, so anything that reorders or reformats these breaks the hover target silently.
        [Test]
        public async Task ShakenEmbarkedUnit_KeepsBothSuffixes_TransportFirst()
        {
            DataBinding<UnitData> transport = MakeUnit("Rhino", new Position(10f, 10f));
            DataBinding<UnitData> cargo = MakeUnit("Warriors", new Position(0f, 0f));
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
            Shake(cargo);
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { transport, cargo }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { transport, cargo });

            Assert.That(OptionName(request, cargo), Is.EqualTo("Warriors (in Rhino) (Shaken - recovers)"));
        }

        // The badge applies to greyed-out rows too: a Shaken unit that has already activated still explains
        // why its activation did nothing, which is the question the player is actually asking.
        [Test]
        public async Task ActivatedShakenUnit_InvalidOptionAlsoCarriesTheBadge()
        {
            DataBinding<UnitData> shaken = MakeUnit("Blade Squad", new Position(20f, 20f));
            DataBinding<UnitData> other = MakeUnit("Gun Squad", new Position(30f, 20f));
            Shake(shaken);
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { shaken, other }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: new[] { other });

            SelectionRequest<UnitData>.InvalidOption invalid =
                request.InvalidOptions.Single(o => o.Option == shaken);
            Assert.That(invalid.Name, Is.EqualTo("Blade Squad (Shaken - recovers)"));
            Assert.That(invalid.Reason, Is.EqualTo("Already activated."));
        }

        // The suffix constant is what the GUI searches for inside a finished label, and the description is
        // the hover body. Both are ASCII-only game-facing text (CLAUDE.md).
        [Test]
        public void BadgeText_IsAsciiAndMatchesTheTokenCatalog()
        {
            Assert.That(UnitStatusLabel.ShakenSuffix, Is.EqualTo("(Shaken - recovers)"));
            Assert.That(UnitStatusLabel.ShakenDescription,
                Is.EqualTo(TokenDefinitionCatalog.Lookup(TokenType.Shaken).Description),
                "the hover must quote the token catalog, not a second copy of the same rule.");

            foreach (string text in new[] { UnitStatusLabel.ShakenSuffix, UnitStatusLabel.ShakenDescription })
                Assert.That(text.All(c => c <= 0x7F), Is.True,
                    $"game-facing text must be ASCII (the ImGui atlas bakes no more): {text}");
        }

        private static void Shake(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

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
