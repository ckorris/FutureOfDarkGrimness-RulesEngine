using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Regression: a Back/cancel on a MANDATORY selection (choosing which unit to activate) resolved the
    // SelectionRequest with null, which the networked reply path treated as a fatal deserialization failure
    // and crashed the game. Such selections now mark AllowCancel=false so the GUI hides Back; the default
    // stays cancellable for selections with a real back-destination (e.g. choosing a melee defender).
    [TestFixture]
    public class MandatorySelectionTests
    {
        [Test]
        public void SelectionRequest_DefaultsToCancellable()
        {
            var request = new SelectionRequest<UnitData>(new PlayerID(System.Guid.NewGuid()), "Pick",
                new List<SelectionRequest<UnitData>.ValidOption>(),
                new List<SelectionRequest<UnitData>.InvalidOption>());

            Assert.That(request.AllowCancel, Is.True);
        }

        [Test]
        public async Task ChooseUnitToActivate_MarksSelectionNonCancellable()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var requester = new CapturingUnitSelectionRequester();
            var ctx = new WoundTestContext(store, requester);

            var player = new PlayerID(System.Guid.NewGuid());
            DataBinding<UnitData> unit = MakeUnit(store, player);

            var turn = new SingleTurnContext(ctx, player, new List<DataBinding<UnitData>> { unit });

            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToMainUnitAction.Bind("done");
            await stage.Enter(turn);

            Assert.That(requester.Captured, Is.Not.Null, "the stage issued a unit-selection request");
            Assert.That(requester.Captured!.AllowCancel, Is.False,
                "choosing which unit to activate is mandatory — no Back/cancel");
        }

        private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID player)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new List<SpecialRule>(), new Position(5f, 5f), store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(player, "Unit", quality: 4, defense: 4,
                specialRules: new List<SpecialRule>(),
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Captures the unit-selection request and answers it with the first valid option so the stage completes.
    internal sealed class CapturingUnitSelectionRequester : IPlayerRequestByID
    {
        public SelectionRequest<UnitData>? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> selection)
            {
                Captured = selection;
                return Task.FromResult((TReply)(object)selection.ValidOptions[0].Option);
            }
            throw new System.InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
