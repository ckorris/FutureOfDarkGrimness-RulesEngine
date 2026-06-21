using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test: the real ChooseUnitToActivateStage explains WHY an off-table
    // Ambush reserve can't be activated, instead of mislabelling it "already activated". An unplaced
    // unit with a later-round defer rule is in reserve, and on round 1 it cannot arrive at all (Ambush
    // arrives from round 2). The stage surfaces those two reasons; the round number is read from the
    // GameProgressData in the store (absent → treated as round 1).
    [TestFixture]
    public class AmbushActivationReasonTests
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
        public async Task RoundOne_AmbushReserve_ReasonExplainsTheRoundGate()
        {
            (DataBinding<UnitData> reserve, DataBinding<UnitData> active) = MakeArmy();

            SelectionRequest<UnitData> request = await RunStage(unactivated: active);

            SelectionRequest<UnitData>.InvalidOption invalid =
                request.InvalidOptions.Single(o => o.Option == reserve);
            Assert.That(invalid.Reason, Is.EqualTo("Ambush reserves can't arrive until round 2."),
                "a round-1 Ambush reserve can't arrive yet — say so, don't call it 'already activated'.");
        }

        [Test]
        public async Task RoundTwo_AmbushReserve_ReasonSaysStillInReserve()
        {
            (DataBinding<UnitData> reserve, DataBinding<UnitData> active) = MakeArmy();
            SetRound(2);

            SelectionRequest<UnitData> request = await RunStage(unactivated: active);

            SelectionRequest<UnitData>.InvalidOption invalid =
                request.InvalidOptions.Single(o => o.Option == reserve);
            Assert.That(invalid.Reason, Is.EqualTo("In Ambush reserve (not yet deployed)."),
                "from round 2 the reserve could arrive but hasn't been deployed — reflect that, not activation.");
        }

        [Test]
        public async Task PlacedActivatedUnit_KeepsTheAlreadyActivatedReason()
        {
            // A normal on-table unit that simply isn't in the unactivated list keeps the plain reason —
            // the reserve detection must not swallow the ordinary "already activated" case.
            (DataBinding<UnitData> _, DataBinding<UnitData> active) = MakeArmy();
            DataBinding<UnitData> spent = MakeUnit("Spent", new Position(15f, 15f), ambush: false);
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { spent }));

            SelectionRequest<UnitData> request = await RunStage(unactivated: active);

            SelectionRequest<UnitData>.InvalidOption invalid =
                request.InvalidOptions.Single(o => o.Option == spent);
            Assert.That(invalid.Reason, Is.EqualTo("This unit has already activated."));
        }

        private async Task<SelectionRequest<UnitData>> RunStage(DataBinding<UnitData> unactivated)
        {
            var requester = new CapturingSelectionRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new ChooseUnitToActivateStage(ctx, new NoOpLayer<ISingleTurnContext>());
            stage.ToMainUnitAction.Bind("done");

            var turnContext = new SingleTurnContext(ctx, _player,
                new List<DataBinding<UnitData>> { unactivated });
            await stage.Enter(turnContext);

            Assert.That(requester.Captured, Is.Not.Null, "the stage issued a unit-selection request");
            return requester.Captured!;
        }

        // One army for _player: an unplaced Ambush reserve plus a placed, activatable unit (the latter is
        // the only valid option, so the capturing requester has something to return and the stage finishes).
        private (DataBinding<UnitData> reserve, DataBinding<UnitData> active) MakeArmy()
        {
            DataBinding<UnitData> reserve = MakeUnit("Infiltrators", new Position(0f, 0f), ambush: true);
            DataBinding<UnitData> active = MakeUnit("Warriors", new Position(10f, 10f), ambush: false);
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { reserve, active }));
            return (reserve, active);
        }

        private DataBinding<UnitData> MakeUnit(string name, Position pos, bool ambush)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (ambush)
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Ambush", CoreRuleCatalog.Ambush));

            return binding;
        }

        private void SetRound(int round)
        {
            var progress = new GameProgressData(EResumeStage.MainPhase, round,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault());
            _store.Create(progress);
        }
    }

    // Captures the unit-selection request for assertions, then activates the first valid option so the
    // stage can complete.
    internal sealed class CapturingSelectionRequester : IPlayerRequestByID
    {
        public SelectionRequest<UnitData>? Captured { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> selection)
            {
                Captured = selection;
                DataBinding<UnitData> choice = selection.ValidOptions[0].Option;
                return Task.FromResult((TReply)(object)choice);
            }
            throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
        }
    }
}
