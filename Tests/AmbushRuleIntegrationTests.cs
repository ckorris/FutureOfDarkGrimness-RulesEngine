using System.Linq;
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
    //  - On arrival the unit is stamped with the ArrivedFromReserve token (#064: the seize-exclusion
    //    marker that ObjectiveOwnershipTests relies on being set here).
    //  - A unit without a later-round defer rule is never offered, even at round 2 (#064).
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
            Assert.That(ambush.GetValue().GetIsOnBattlefield(), Is.False,
                "a reserve is not on the battlefield, so it can't be activated or targeted.");
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
            Assert.That(Rules.Dispatch.ReserveRules.IsInReserve(ambush.GetValue()), Is.False,
                "arriving clears the reserve state.");
            Assert.That(ambush.GetValue().GetIsOnBattlefield(), Is.True, "it is now in play.");
        }

        [Test]
        public async Task RoundTwo_Decline_StaysReserved()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: false, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 2);

            Assert.That(requester.PlaceRequest, Is.Null, "declining means no placement");
            AssertAllAtOrigin(ambush, "a declined Ambush unit stays in reserve");
            Assert.That(Rules.Dispatch.ReserveRules.IsInReserve(ambush.GetValue()), Is.True,
                "declining leaves the reserve state in place, so it is offered again next round.");
        }

        [Test]
        public async Task RoundTwo_Accept_StampsArrivedFromReserveToken()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 2);

            Assert.That(ambush.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.ArrivedFromReserve), Is.True,
                "an arrived reserve unit carries the seize-exclusion marker for the round it arrives.");
        }

        // #309: a networked client's renderer snapshots the unit's battlefield status from the
        // replicated state at the moment each model position lands (the position binding's
        // OnValueChanged - the same event ridden here). The reserve clear must therefore replicate
        // BEFORE the positions, or the client captures a still-reserved unit and renders the
        // arrival label-only until it next moves.
        [Test]
        public async Task RoundTwo_Accept_ReserveClearsBeforeFirstPositionReplicates()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            var onBattlefieldAtEachUpdate = new List<bool>();
            foreach (DataBinding<ModelData> model in ambush.GetValue().ModelBindings)
            {
                model.GetValue().PositionBinding.OnValueChanged +=
                    (_, _) => onBattlefieldAtEachUpdate.Add(ambush.GetValue().GetIsOnBattlefield());
            }

            await RunStage(requester, roundCount: 2);

            Assert.That(onBattlefieldAtEachUpdate, Is.Not.Empty, "the arrival repositions the models");
            Assert.That(onBattlefieldAtEachUpdate, Is.All.True,
                "every replicated position update must already see the unit on the battlefield");
        }

        // #399: the arrival is presented, so a front-end can play something at the spots the models
        // came down on (the app draws a dust cloud) and a networked opponent sees the same thing at the
        // same point in the play-by-play.
        [Test]
        public async Task RoundTwo_Accept_PresentsTheArrival_AtEveryModelsLandingSpot()
        {
            DataBinding<UnitData> ambush = MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);
            var sink = new BeatCollectingSink();

            await RunStage(requester, roundCount: 2, sink);

            var arrival = sink.Beats.OfType<Presentation.Beats.UnitArrivedBeat>().SingleOrDefault();
            Assert.That(arrival, Is.Not.Null, "an ambush that lands must announce itself as a beat");
            Assert.That(arrival!.Unit, Is.EqualTo(ambush.GetValue().ID));
            Assert.That(arrival.ReserveRuleName, Is.EqualTo("Ambush"),
                "the beat names the rule that brought it on, not a hard-coded 'Ambush'");
            Assert.That(arrival.Models, Has.Count.EqualTo(ambush.GetValue().ModelBindings.Count));

            // The positions must be where the models ACTUALLY are, not where they were when the stage
            // started - the beat is emitted after the placement is applied, so a cloud drawn from it
            // sits on the unit rather than back at the origin.
            foreach (Presentation.Beats.ArrivedModel arrived in arrival.Models)
            {
                ModelData model = ambush.GetValue().ModelBindings
                    .Select(b => b.GetValue()).Single(m => m.ID.Equals(arrived.Model));
                Assert.That(arrived.Position.x, Is.EqualTo(model.Position.x).Within(0.0001f));
                Assert.That(arrived.Position.z, Is.EqualTo(model.Position.z).Within(0.0001f));
                Assert.That(arrived.Position.x == 0f && arrived.Position.z == 0f, Is.False,
                    "(0,0) is the off-table origin - the beat would puff dust off the board");
            }
        }

        [Test]
        public async Task RoundTwo_Decline_PresentsNoArrival()
        {
            MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: false, destX: 20f, destZ: 20f);
            var sink = new BeatCollectingSink();

            await RunStage(requester, roundCount: 2, sink);

            Assert.That(sink.Beats.OfType<Presentation.Beats.UnitArrivedBeat>(), Is.Empty,
                "a unit that stayed in reserve never arrived");
        }

        [Test]
        public async Task RoundOne_PresentsNoArrival()
        {
            MakeAmbushUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);
            var sink = new BeatCollectingSink();

            await RunStage(requester, roundCount: 1, sink);

            Assert.That(sink.Beats.OfType<Presentation.Beats.UnitArrivedBeat>(), Is.Empty,
                "core Ambush cannot arrive in round 1, so nothing is presented");
        }

        private sealed class BeatCollectingSink : Presentation.IPresentationSink
        {
            public List<Presentation.PresentationBeat> Beats { get; } = new();

            public void OnBeat(Presentation.PresentationBeat beat) => Beats.Add(beat);
        }

        [Test]
        public async Task RoundTwo_NonReserveUnit_NotOfferedAndUntouched()
        {
            DataBinding<UnitData> plain = MakePlainUnit();
            var requester = new AmbushArrivalRequester(accept: true, destX: 20f, destZ: 20f);

            await RunStage(requester, roundCount: 2);

            Assert.That(requester.PlaceRequest, Is.Null, "a unit with no later-round defer rule is never offered");
            AssertAllAtOrigin(plain, "a non-reserve unit is left untouched");
            Assert.That(plain.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.ArrivedFromReserve), Is.False);
        }

        private async Task RunStage(IPlayerRequestByID requester, int roundCount,
            Presentation.IPresentationSink? sink = null)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, presentationSink: sink);
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
            // The player held it back at deployment: reserve is explicit unit state, not an origin position.
            Rules.Dispatch.ReserveRules.PlaceInReserve(binding.GetValue());

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Same shape as MakeAmbushUnit but with no special rules attached, so it has no later-round
        // defer and the stage never offers it.
        private DataBinding<UnitData> MakePlainUnit()
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), 
                    new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Warriors", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

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
                    return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
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
