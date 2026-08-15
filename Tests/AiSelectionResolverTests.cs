using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A5-10 (owner's reversal of #335, 2026-08-15): the AI ACCEPTS the deploy-time embark prompt -
    // "during deployment, it's almost always best to put something in transports". The prompt is still
    // keyed on the shared DEPLOY_NORMALLY_CHOICE label (a blanket rule over cancellable selections would
    // loop the prompts that re-ask after a cancel); the get-out half of the ride lives in
    // AiStringSelectionResolver.ShouldDisembark. Also here: transports deploy first (A5-10), so later
    // cargo actually receives the offer.
    [TestFixture]
    public class AiSelectionResolverTests
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
        public async Task Resolve_DeployTimeEmbarkPrompt_TakesTheFirstTransport()
        {
            SelectionRequest<UnitData> request = Request(allowCancel: true,
                cancelLabel: ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);

            DataBinding<UnitData> choice = await new AiSelectionResolver<UnitData>().Resolve(request);

            Assert.That(choice, Is.EqualTo(request.ValidOptions[0].Option),
                "the AI rides: first offered transport, never a decline (#191 A5-10).");
        }

        // A5-10's other half: cargo only gets the offer if its ride is already on the table, so the
        // deploy-order pick puts transports first even when they are not first in the list.
        [Test]
        public async Task Resolve_DeployOrder_PicksATransportFirst()
        {
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            DataBinding<UnitData> squad = Unit("Grunts");
            DataBinding<UnitData> transport = Transport("Rhino", capacity: 6);
            var request = new SelectionRequest<UnitData>(_player,
                ChooseUnitToDeployStage.CHOOSE_UNIT_INSTRUCTIONS,
                new List<SelectionRequest<UnitData>.ValidOption>
                {
                    new(squad, "Grunts"), // listed first - front-of-list would deploy the cargo early
                    new(transport, "Rhino"),
                },
                new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: false);

            DataBinding<UnitData> choice = await new AiSelectionResolver<UnitData>(evaluator).Resolve(request);

            Assert.That(choice, Is.EqualTo(transport),
                "the transport goes down first so later cargo can be offered the ride.");
        }

        // The guard rail: an ordinary cancellable selection (melee defender) must still be ANSWERED.
        // Cancelling one returns to Choose Action, which re-offers it - an infinite loop, not a decline.
        [Test]
        public async Task Resolve_OrdinaryCancellableSelection_StillPicksAnOption()
        {
            SelectionRequest<UnitData> request = Request(allowCancel: true, cancelLabel: null);

            DataBinding<UnitData> choice = await new AiSelectionResolver<UnitData>().Resolve(request);

            Assert.That(choice, Is.EqualTo(request.ValidOptions[0].Option));
        }

        [Test]
        public async Task Resolve_MandatorySelection_PicksTheFirstOption()
        {
            SelectionRequest<UnitData> request = Request(allowCancel: false, cancelLabel: null);

            DataBinding<UnitData> choice = await new AiSelectionResolver<UnitData>().Resolve(request);

            Assert.That(choice, Is.EqualTo(request.ValidOptions[0].Option));
        }

        private SelectionRequest<UnitData> Request(bool allowCancel, string? cancelLabel)
        {
            var options = new List<SelectionRequest<UnitData>.ValidOption>
            {
                new(Unit("Rhino"), "Embark into Rhino"),
                new(Unit("Chimera"), "Embark into Chimera"),
            };

            return new SelectionRequest<UnitData>(_player, "Pick one.", options,
                new List<SelectionRequest<UnitData>.InvalidOption>(),
                allowCancel: allowCancel, displayName: null, cancelLabel: cancelLabel);
        }

        private DataBinding<UnitData> Transport(string name, int capacity)
        {
            DataBinding<UnitData> binding = Unit(name);
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(
                TransportUtilities.TransportRuleName, CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return binding;
        }

        private DataBinding<UnitData> Unit(string name)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
