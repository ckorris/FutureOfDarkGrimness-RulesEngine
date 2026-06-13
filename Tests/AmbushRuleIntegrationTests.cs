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
    // Vertical-slice integration test for #042 Phase 7h (deploy primitive): proves Ambush stays in
    // reserve and arrives from round 2 onward through the real StartOfRoundExtraActionStage.
    //  - Round gate: nothing arrives in round 1.
    //  - Arrival: round 2+, on accept the unit is placed and the request carries the 9" min-enemy-distance.
    //  - Decline: the unit stays in reserve.
    // The faithful ">9" from enemies" enforcement lives in the place resolvers (the integration test uses
    // a canned requester); AiPlaceObjectsResolverTests proves that enforcement deterministically.
    [TestFixture]
    public class AmbushRuleIntegrationTests
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
        public async Task RoundOne_NoArrival()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 1);

            Assert.That(requester.PlaceRequest, Is.Null, "no placement is offered in round 1");
            AssertAllAtOrigin(ambush, "Ambush unit stays in reserve in round 1");
        }

        [Test]
        public async Task RoundTwo_Accept_ArrivesWithEnemyDistanceConstraint()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 2);

            Assert.That(requester.PlaceRequest, Is.Not.Null, "round 2 offers the reserve placement");
            Assert.That(requester.PlaceRequest!.MinDistanceFromEnemiesInches, Is.EqualTo(9f).Within(0.001f),
                "Ambush places over 9\" from enemies");
            foreach (DataBinding<ModelData> model in ambush.GetValue().ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                Assert.That(pos.x, Is.EqualTo(20f).Within(0.001f));
                Assert.That(pos.z, Is.EqualTo(20f).Within(0.001f));
            }
        }

        [Test]
        public async Task RoundTwo_Decline_StaysReserved()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: false, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 2);

            Assert.That(requester.PlaceRequest, Is.Null, "declining means no placement");
            AssertAllAtOrigin(ambush, "a declined Ambush unit stays in reserve");
        }

        private async Task RunStage(IPlayerRequestByID requester, int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }

        private static void AssertAllAtOrigin(DataBinding<UnitData> unit, string because)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                Position pos = model.GetValue().PositionBinding.GetValue();
                Assert.That(pos.x == 0f && pos.z == 0f, Is.True, because);
            }
        }

        private DataBinding<UnitData> MakeAmbushUnit()
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Infiltrators", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Ambush", CoreRuleCatalog.Ambush));

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // Answers the round-start YesNo offer with a fixed choice, and (on accept) the placement request by
    // dropping every model at a fixed destination, capturing the request for assertions.
    internal sealed class AmbushArrivalRequester : IPlayerRequestByID
    {
        private readonly bool _accept;
        private readonly float _destX, _destZ;
        public PlaceObjectsRequest<ModelData>? PlaceRequest { get; private set; }

        public AmbushArrivalRequester(bool accept, float destX, float destZ)
        {
            _accept = accept; _destX = destX; _destZ = destZ;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case YesNoRequest:
                    return Task.FromResult((TReply)(object)_accept);
                case PlaceObjectsRequest<ModelData> place:
                    PlaceRequest = place;
                    var dest = new Position(_destX, _destZ);
                    var entries = place.ModelsToPlace
                        .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                        .ToList();
                    return Task.FromResult((TReply)(object)entries);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }

    // Minimal IMainPhaseContext with a settable round number.
    internal sealed class TestMainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; }
        public int RoundCount { get; }
        public List<ITeam> TeamActivateOrder { get; } = new();

        public TestMainPhaseContext(IGameContext gameContext, int roundCount)
        {
            GameContext = gameContext;
            RoundCount = roundCount;
        }

        public void OnEndOfRound(IReadOnlyList<ITeam> newTeamActivateOrder) { }
    }
}
