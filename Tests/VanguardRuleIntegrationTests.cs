using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #426 vertical slice for the Core Rules v3.6.0 Vanguard / Fanatic text: "After this model is deployed, it
    // may be placed anywhere fully within 9" of its position, over 9" away from non-Vanguard enemy units."
    // (3.6 added the enemy clause; Fanatic's text says "non-Vanguard" too.) Vanguard was a 9" triggered MOVE
    // until #426; it is now Fanatic's placement shape. Drives the REAL DeployUnitStage (deploy placement ->
    // Deployment_OnUnitDeployed offer -> reposition fold) and judges the reposition request with the shared
    // validator every resolver uses (PlacementDistanceRules), then lets the real AI resolver answer it.
    [TestFixture]
    public class VanguardRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _us, _them;
        private TeamData _ourTeam = null!, _theirTeam = null!;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _ourTeam = new TeamData(0, new List<PlayerID> { _us });
            _theirTeam = new TeamData(1, new List<PlayerID> { _them });

            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float d = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_ourTeam] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, 0f, d))),
                [_theirTeam] = _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, w, h - d, h))),
            };
        }

        private static IEnumerable<TestCaseData> Rules()
        {
            yield return new TestCaseData("Vanguard").SetName("Catalog_Vanguard_Is36Placement");
            yield return new TestCaseData("Fanatic").SetName("Catalog_Fanatic_Is36Placement");
        }

        [TestCaseSource(nameof(Rules))]
        public void Catalog_IsADeployPlacement_WithTheNonVanguardClause(string ruleName)
        {
            SpecialRuleDefinition rule = Definition(ruleName);
            Assert.That(rule.Description, Is.EqualTo(
                "After this model is deployed, it may be placed anywhere fully within 9\" of its position, " +
                "over 9\" away from non-Vanguard enemy units."));

            ActivatedAbility ability = rule.Activated.Single();
            Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Deployment_OnUnitDeployed));
            Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());
            Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.MostModelsHaveThisRule>(),
                "the whole unit is re-placed, so most models must have the rule (#428).");
            var effect = (Effect.RepositionOnDeploy)ability.Effect;
            Assert.That(effect.MaxInches, Is.EqualTo(9f));
            Assert.That(effect.EnemyClearanceInches, Is.EqualTo(9f));
            Assert.That(effect.ExemptEnemyRule, Is.EqualTo("Vanguard"),
                "the text says non-Vanguard for both rules, implemented as written.");
        }

        [TestCase("Vanguard")]
        [TestCase("Fanatic")]
        public async Task Reposition_NonVanguardEnemyAtEightInches_Rejected_EnemyVanguardAtEightInches_Accepted(
            string ruleName)
        {
            DataBinding<UnitData> ours = MakeUnit(_us, "Strike Team", Definition(ruleName));
            MakeArmy(_us, ours);
            DataBinding<UnitData> grunts = MakeUnit(_them, "Grunts", rule: null);
            PlaceAt(grunts, new Position(36f, 20f));
            DataBinding<UnitData> raiders = MakeUnit(_them, "Raiders", CoreRuleCatalog.Vanguard);
            PlaceAt(raiders, new Position(56f, 20f));
            MakeArmy(_them, grunts, raiders);

            var requester = new RepositionCapturingRequester(deployAt: new Position(36f, 5f));
            await RunDeployUnit(requester, ours);

            PlaceObjectsRequest<ModelData> request = requester.Reposition!;
            Assert.That(request, Is.Not.Null, "the reposition placement was offered.");
            Assert.That(request.MaxDistanceFromStartInches, Is.EqualTo(9f), "fully within 9\" of its position.");
            var noFlat = new List<Position>();
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(36f, 12f), noFlat),
                Is.True, "8\" from a non-Vanguard enemy unit is too close.");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(36f, 11f), noFlat),
                Is.False, "exactly 9\" is legal ('over 9\"' is exclusive at the boundary).");
            Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, new Position(56f, 12f), noFlat),
                Is.False, "8\" from an enemy VANGUARD unit is fine - it does not push back.");
            Assert.That(request.EnemyKeepOutDiscs, Has.Count.EqualTo(grunts.GetValue().ModelBindings.Count));
            Assert.That(request.EnemyKeepOutNote, Does.Contain("non-Vanguard").And.Contain("9\""));
            Assert.That(request.TaskName, Is.EqualTo(RepositionPlacement.DeployRepositionTaskName));
        }

        [Test]
        public async Task Reposition_ExemptionReadsAGrantedVanguard_Too()
        {
            DataBinding<UnitData> ours = MakeUnit(_us, "Strike Team", CoreRuleCatalog.Vanguard);
            MakeArmy(_us, ours);
            DataBinding<UnitData> granted = MakeUnit(_them, "Escorts", rule: null);
            granted.GetValue().Tokens.AddToken(new Rules.Tokens.Token(TokenType.RuleGrant, 1,
                new TokenClearTrigger.ManualOnly(),
                new Rules.Tokens.TokenPayload.RuleGrant("Vanguard", ELifetime.UntilEndOfGame)));
            PlaceAt(granted, new Position(36f, 20f));
            MakeArmy(_them, granted);

            var requester = new RepositionCapturingRequester(deployAt: new Position(36f, 5f));
            await RunDeployUnit(requester, ours);

            Assert.That(requester.Reposition!.EnemyKeepOutDiscs, Is.Empty,
                "a unit that gained Vanguard (aura / grant) is a Vanguard unit too.");
        }

        [Test]
        public async Task Ai_TakesTheReposition_AndStopsOverNineInchesFromTheEnemy()
        {
            DataBinding<UnitData> ours = MakeUnit(_us, "Strike Team", CoreRuleCatalog.Vanguard);
            MakeArmy(_us, ours);
            // Deployed 15" short of a non-Vanguard enemy: a full 9" advance would end 6" from it, so the bot
            // has to back off to a legal spot over 9" away rather than just take its full reach.
            DataBinding<UnitData> grunts = MakeUnit(_them, "Grunts", rule: null);
            PlaceAt(grunts, new Position(36f, 20f));
            MakeArmy(_them, grunts);

            var tableState = new TableState(_store);
            var requester = new AiBackedRequester(new AiPlaceObjectsResolver<ModelData>(tableState),
                deployAt: new Position(36f, 5f));
            await RunDeployUnit(requester, ours);

            List<ModelData> models = ours.GetValue().ModelBindings.Select(b => b.GetValue()).ToList();
            List<Position> enemy = grunts.GetValue().ModelBindings.Select(b => b.GetValue().Position).ToList();
            for (int i = 0; i < models.Count; i++)
            {
                Position start = requester.DeploySpots[i];
                Position end = models[i].Position;
                float moved = Position.GetDistance2D(start, end);
                Assert.That(moved, Is.GreaterThan(1f), "the bot advanced instead of declining the reposition.");
                Assert.That(moved, Is.LessThanOrEqualTo(9f), "inside each model's 9\" radius.");
                foreach (Position e in enemy)
                {
                    Assert.That(Position.GetDistance2D(end, e), Is.GreaterThanOrEqualTo(9f),
                        $"model {i} stopped over 9\" from the non-Vanguard enemy.");
                }
            }
        }

        // --- helpers ---

        private static SpecialRuleDefinition Definition(string name) =>
            name == "Vanguard" ? CoreRuleCatalog.Vanguard : CoreRuleCatalog.Fanatic;

        private async Task RunDeployUnit(IPlayerRequestByID requester, DataBinding<UnitData> unit)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, new List<ITeam> { _ourTeam, _theirTeam }, _zones)
            {
                CurrentDeployingUnit = unit,
            };
            var stage = new DeployUnitStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("finish");
            await stage.Enter(deployment);
        }

        private static void PlaceAt(DataBinding<UnitData> unit, Position at)
        {
            int i = 0;
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().SetPosition(new Position(at.x + i * 1.1f, at.z));
                i++;
            }
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, SpecialRuleDefinition? rule)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null) binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            return binding;
        }

        private void MakeArmy(PlayerID owner, params DataBinding<UnitData>[] units) =>
            _store.Create(new ArmyData(owner, units.ToList()));

        // Accepts the deploy ability, deploys the unit in a line at deployAt, and captures the reposition request
        // (answering it by standing still).
        private sealed class RepositionCapturingRequester : IPlayerRequestByID
        {
            private readonly Position _deployAt;
            public PlaceObjectsRequest<ModelData>? Reposition { get; private set; }

            public RepositionCapturingRequester(Position deployAt) => _deployAt = deployAt;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        return Task.FromResult((TReply)(object)true);
                    case PlaceObjectsRequest<ModelData> place:
                        bool reposition = place.MaxDistanceFromStartInches > 0f;
                        if (reposition) Reposition = place;
                        var entries = place.ModelsToPlace.Select((m, i) => new PlacedObjectEntry<ModelData>(m,
                            reposition ? m.GetValue().Position : new Position(_deployAt.x + i * 1.1f, _deployAt.z)))
                            .ToList();
                        return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
                    default:
                        throw new InvalidOperationException("Unexpected request: " + request.GetType());
                }
            }
        }

        // Says yes to the deploy ability, deploys the unit in a line at deployAt (remembering each spot), and
        // lets the real AI resolver answer the reposition.
        private sealed class AiBackedRequester : IPlayerRequestByID
        {
            private readonly AiPlaceObjectsResolver<ModelData> _ai;
            private readonly Position _deployAt;
            public List<Position> DeploySpots { get; } = new();

            public AiBackedRequester(AiPlaceObjectsResolver<ModelData> ai, Position deployAt)
            {
                _ai = ai;
                _deployAt = deployAt;
            }

            public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case YesNoRequest:
                        return (TReply)(object)true;
                    case PlaceObjectsRequest<ModelData> place when place.MaxDistanceFromStartInches > 0f:
                        return (TReply)(object)await _ai.Resolve(place);
                    case PlaceObjectsRequest<ModelData> place:
                        var entries = place.ModelsToPlace.Select((m, i) => new PlacedObjectEntry<ModelData>(m,
                            new Position(_deployAt.x + i * 1.1f, _deployAt.z))).ToList();
                        DeploySpots.AddRange(entries.Select(e => e.Position));
                        return (TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries);
                    default:
                        throw new InvalidOperationException("Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
