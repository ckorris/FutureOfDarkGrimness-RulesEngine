using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #426 vertical slice for the clause Scout and Ambush share in both 3.5 and Core Rules v3.6.0: "Players
    // alternate in placing Scout/Ambush units, starting with the player that activates next." Neither pass
    // implemented it (Scouts landed in list order, Ambush arrivals in army-store order). Drives the REAL
    // PlaceDeferredUnitsStage and StartOfRoundExtraActionStage with two players and records the order the
    // placements are asked for.
    [TestFixture]
    public class PlacementAlternationRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _a, _b;
        private TeamData _teamA = null!, _teamB = null!;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _a = new PlayerID(Guid.NewGuid());
            _b = new PlayerID(Guid.NewGuid());
            _teamA = new TeamData(0, new List<PlayerID> { _a });
            _teamB = new TeamData(1, new List<PlayerID> { _b });

            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_teamA] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, 0f, d))),
                [_teamB] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, h - d, h))),
            };
        }

        [Test]
        public async Task Scouts_Alternate_StartingWithThePlayerThatActivatesNext()
        {
            // A's army is created first, so store/list order would place A, A, B, B.
            MakeArmy(_a, MakeUnit(_a, "A1", CoreRuleCatalog.Scout), MakeUnit(_a, "A2", CoreRuleCatalog.Scout));
            MakeArmy(_b, MakeUnit(_b, "B1", CoreRuleCatalog.Scout), MakeUnit(_b, "B2", CoreRuleCatalog.Scout),
                MakeUnit(_b, "B3", CoreRuleCatalog.Scout));

            var requester = new OrderRecordingRequester(this);
            // B won the deployment roll-off: B activates first in round 1, so B places the first Scout.
            await RunScoutPass(requester, _teamB, _teamA);

            Assert.That(requester.Placed, Is.EqualTo(new[] { "B1", "A1", "B2", "A2", "B3" }),
                "one Scout unit per turn, alternating from the player that activates next; a player who " +
                "runs out is skipped and the other places the rest.");
        }

        [Test]
        public async Task Scouts_TheOtherRollOrder_FlipsWhoGoesFirst()
        {
            MakeArmy(_a, MakeUnit(_a, "A1", CoreRuleCatalog.Scout), MakeUnit(_a, "A2", CoreRuleCatalog.Scout));
            MakeArmy(_b, MakeUnit(_b, "B1", CoreRuleCatalog.Scout), MakeUnit(_b, "B2", CoreRuleCatalog.Scout));

            var requester = new OrderRecordingRequester(this);
            await RunScoutPass(requester, _teamA, _teamB);

            Assert.That(requester.Placed, Is.EqualTo(new[] { "A1", "B1", "A2", "B2" }));
        }

        [Test]
        public async Task AmbushArrivals_Alternate_StartingWithThePlayerThatActivatesNext()
        {
            MakeArmy(_a, MakeReserve(_a, "A1"), MakeReserve(_a, "A2"));
            MakeArmy(_b, MakeReserve(_b, "B1"), MakeReserve(_b, "B2"));

            var requester = new OrderRecordingRequester(this);
            // B finished activating first last round, so B activates first this round.
            await RunRoundStart(requester, roundCount: 2, _teamB, _teamA);

            Assert.That(requester.Placed, Is.EqualTo(new[] { "B1", "A1", "B2", "A2" }),
                "one arrival per turn, alternating from the player that activates next.");
            Assert.That(requester.Asked, Is.EqualTo(new[] { "B1", "A1", "B2", "A2" }));
        }

        [Test]
        public async Task AmbushArrivals_ADeclineIsNotATurn_AndIsNotAskedAgainThisRound()
        {
            MakeArmy(_a, MakeReserve(_a, "A1"), MakeReserve(_a, "A2"));
            MakeArmy(_b, MakeReserve(_b, "B1"), MakeReserve(_b, "B2"));

            var requester = new OrderRecordingRequester(this) { Decline = { "A1" } };
            await RunRoundStart(requester, roundCount: 2, _teamA, _teamB);

            Assert.That(requester.Asked, Is.EqualTo(new[] { "A1", "A2", "B1", "B2" }),
                "declining A1 hands A the next unit in the same turn; A1 is not offered again this round.");
            Assert.That(requester.Placed, Is.EqualTo(new[] { "A2", "B1", "B2" }));
        }

        // --- helpers ---

        private async Task RunScoutPass(IPlayerRequestByID requester, ITeam first, ITeam second)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { first, second }, _zones);
            var stage = new PlaceDeferredUnitsStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("done");
            await stage.Enter(deployment);
        }

        private async Task RunRoundStart(IPlayerRequestByID requester, int roundCount, ITeam first, ITeam second)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var main = new TestMainPhaseContext(ctx, roundCount);
            main.TeamActivateOrder.Add(first);
            main.TeamActivateOrder.Add(second);
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(main);
        }

        internal string NameOfUnitPlacing(PlaceObjectsRequest<ModelData> request)
        {
            ModelData first = request.ModelsToPlace[0].GetValue();
            return _store.GetAllValues<UnitData>()
                .Single(unit => unit.ModelBindings.Any(m => ReferenceEquals(m.GetValue(), first))).Name;
        }

        private DataBinding<UnitData> MakeReserve(PlayerID owner, string name)
        {
            DataBinding<UnitData> unit = MakeUnit(owner, name, CoreRuleCatalog.Ambush);
            ReserveRules.PlaceInReserve(unit.GetValue());
            return unit;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, Rules.Definitions.SpecialRuleDefinition rule)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return binding;
        }

        private void MakeArmy(PlayerID owner, params DataBinding<UnitData>[] units) =>
            _store.Create(new ArmyData(owner, units.ToList()));

        // Records which unit each placement / Ambush offer was for, in order. Each placement lands on its own
        // spot (spread along x by call count) so nothing overlaps and the commit guard never steps in.
        private sealed class OrderRecordingRequester : IPlayerRequestByID
        {
            private readonly PlacementAlternationRuleIntegrationTests _owner;
            public List<string> Placed { get; } = new();
            public List<string> Asked { get; } = new();
            public HashSet<string> Decline { get; } = new();

            public OrderRecordingRequester(PlacementAlternationRuleIntegrationTests owner) => _owner = owner;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest yesNo:
                        string asked = yesNo.QuestionText.Replace("Deploy ", "").Replace(" from Ambush this round?", "");
                        Asked.Add(asked);
                        return Task.FromResult((TReply)(object)!Decline.Contains(asked));
                    case PlaceObjectsRequest<ModelData> place:
                        Placed.Add(_owner.NameOfUnitPlacing(place));
                        ZoneBounds b = place.DeploymentZone.Bounds;
                        var at = new Position(b.Left + 4f + 6f * Placed.Count, b.CenterZ);
                        var entries = place.ModelsToPlace
                            .Select(m => new PlacedObjectEntry<ModelData>(m, at)).ToList();
                        return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
                    default:
                        throw new InvalidOperationException("Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
