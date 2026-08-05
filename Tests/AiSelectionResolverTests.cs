using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #331: the AI declines the deploy-time embark prompt (owner's call - embarking needs forethought this
    // AI hasn't got, and it never plans where the cargo gets out). The decline is deliberately narrow: it
    // keys on the shared DEPLOY_NORMALLY_CHOICE label, because a blanket "AI cancels cancellable
    // selections" would loop the prompts that re-ask after a cancel.
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
        public async Task Resolve_DeployTimeEmbarkPrompt_DeclinesAndDeploysNormally()
        {
            SelectionRequest<UnitData> request = Request(allowCancel: true,
                cancelLabel: ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);

            DataBinding<UnitData> choice = await new AiSelectionResolver<UnitData>().Resolve(request);

            Assert.That(choice, Is.Null,
                "null is 'deploy normally' - the AI never takes the transport, even listed first.");
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
