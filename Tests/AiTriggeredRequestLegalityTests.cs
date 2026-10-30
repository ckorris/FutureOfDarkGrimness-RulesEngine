using FDG.Ai.Resolvers;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #442 (the #066 pattern for the 3.6 request shapes the AI answers): every AI reply must be one the engine
    // ACCEPTS. The activation-time reposition family (#434: Wolfborn / Bounding / Rapid Blink / Nimble / Dash,
    // and Wave-Step since #435) raises an optional triggered move at activation start, and GameOperationServices
    // treats an invalid optional path as a silent decline - so an illegal AI answer would never fault a game, it
    // would just quietly cost the unit its move. These run the REAL ActivationStartStage with the AI's resolver
    // answering, and assert the engine applied exactly the path the AI sent (or that the AI declined outright).
    // The 3.6 Tag spend (TargetMarkerSpend's "remove one Tag token" prompt) is pinned the same way.
    [TestFixture]
    public class AiTriggeredRequestLegalityTests
    {
        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // Wolfborn's 3.6 shape: "you may move all models with this rule in it by up to D3"" (the D3 rolls 2 here).
        private static SpecialRuleDefinition Wolfborn() => new("Wolfborn",
            new[]
            {
                new HookEntry(EHookID.Activation_OnActivationStart, new Condition.MostModelsHaveThisRule(),
                    new Effect.RepositionAtActivation(new DiceExpression.D3(), 0, Move: true),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>());

        [Test]
        public async Task SoloResolver_ActivationStartMove_OpenBoard_TheEngineAppliesItsPath()
        {
            DataBinding<UnitData> pack = MakeUnit(_us, "Pack", 10f, 20f, Wolfborn());
            MakeUnit(_them, "Enemy", 30f, 20f);
            var requester = new AiMoveRequester(new AiDefineMovementResolver(new TableState(_store), _us));

            await RunActivationStart(pack, requester);

            AssertEngineAppliedTheReply(pack, requester);
            Assert.That(requester.Reply, Is.InstanceOf<Selected<List<ModelMoveEntry>>>(),
                "an open board has a legal move toward the enemy");
        }

        [Test]
        public async Task SoloResolver_ActivationStartMove_WallInTheWay_IsAppliedOrDeclined_NeverSilentlyRejected()
        {
            DataBinding<UnitData> pack = MakeUnit(_us, "Pack", 10f, 20f, Wolfborn());
            MakeUnit(_them, "Enemy", 30f, 20f);
            // An impassable wall right in front: the straight 2" step toward the enemy clips it.
            _store.Create(new TerrainData(ETerrainType.Impassible, new RectangularZone(11.2f, 13f, 17f, 25f)));
            var requester = new AiMoveRequester(new AiDefineMovementResolver(new TableState(_store), _us));

            await RunActivationStart(pack, requester);

            AssertEngineAppliedTheReply(pack, requester);
        }

        [Test]
        public async Task SoloResolver_ActivationStartMove_EnemyClose_NeverEndsInsideTheStandoff()
        {
            DataBinding<UnitData> pack = MakeUnit(_us, "Pack", 10f, 20f, Wolfborn());
            // 2" from the pack's front model: a full 2" step would end in base contact, which a non-charge move
            // may not do.
            MakeUnit(_them, "Enemy", 13f, 20f);
            var requester = new AiMoveRequester(new AiDefineMovementResolver(new TableState(_store), _us));

            await RunActivationStart(pack, requester);

            AssertEngineAppliedTheReply(pack, requester);
        }

        [Test]
        public async Task TacticianResolver_ActivationStartMove_TheEngineAppliesItsPath()
        {
            DataBinding<UnitData> pack = MakeUnit(_us, "Pack", 10f, 20f, Wolfborn());
            MakeUnit(_them, "Enemy", 30f, 20f);
            var tableState = new TableState(_store);
            var planner = new TacticianPlanner(tableState, new RuleEvaluator(new ProbabilisticDiceRoller()));
            planner.BeginActivation(pack); // what TacticianActivationResolver does when it picks the unit
            var resolver = new TacticianMovementResolver(planner, tableState,
                new AiDefineMovementResolver(tableState, _us));
            var requester = new AiMoveRequester(resolver);

            await RunActivationStart(pack, requester);

            AssertEngineAppliedTheReply(pack, requester);
        }

        // --- the 3.6 Tag spend (#434): "may remove ONE token from their target" per attack ---------------------------

        [Test]
        public async Task SoloResolver_TagSpendPrompt_RepliesWithAnOffer_AndSpendsOneToken()
        {
            DataBinding<UnitData> target = MakeUnit(_them, "Tagged", 20f, 20f);
            target.GetValue().Tokens.AddToken(new Token(TokenType.SingleSpendApBonus, 2, new TokenClearTrigger.ManualOnly()));
            var resolver = new AiStringSelectionResolver(new TableState(_store), _us);
            var requester = new AiStringRequester(resolver);
            var ctx = new TriggeredMoveTestContext(_store, requester);

            int net = await TargetMarkerSpend.ConsumeNet(ctx, _us, target.GetValue(), ERollKind.Save);

            Assert.That(requester.Asked, Is.Not.Null, "the Tag prompt was raised");
            Assert.That(requester.Asked!.ValidOptions, Does.Contain(requester.Reply), "the reply is one of the offers");
            Assert.That(net, Is.EqualTo(1), "the aggressive default: one Tag token for AP(+1)");
            Assert.That(target.GetValue().Tokens.GetTokenCount(TokenType.SingleSpendApBonus), Is.EqualTo(1));
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private async Task RunActivationStart(DataBinding<UnitData> unit, IPlayerRequestByID requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(2));
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(unitContext);
        }

        // The engine accepted the AI's answer iff every model now stands at the end of the path the AI sent; a
        // Cancelled reply is the legal decline and must leave every model where it was.
        private static void AssertEngineAppliedTheReply(DataBinding<UnitData> unit, AiMoveRequester requester)
        {
            Assert.That(requester.Request, Is.Not.Null, "the triggered move was offered");
            Assert.That(requester.Request!.AllowCancel, Is.True, "an optional move");
            Assert.That(requester.Request.MainActivationMove, Is.False, "a triggered move, not the activation's own");

            if (requester.Reply is Selected<List<ModelMoveEntry>> selected)
            {
                foreach (ModelMoveEntry entry in selected.Value)
                {
                    Position expected = entry.Positions.Count > 0 ? entry.Positions[^1] : requester.Starts[entry.Model];
                    Position actual = entry.Model.GetValue().PositionBinding.GetValue();
                    Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f),
                        "the engine rejected the AI's path (an optional move declines silently on an invalid path)");
                    Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
                }
            }
            else
            {
                Assert.That(requester.Reply, Is.InstanceOf<Cancelled<List<ModelMoveEntry>>>());
                foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
                {
                    Position actual = model.GetValue().PositionBinding.GetValue();
                    Assert.That(actual.x, Is.EqualTo(requester.Starts[model].x).Within(0.001f), "declined: unmoved");
                    Assert.That(actual.z, Is.EqualTo(requester.Starts[model].z).Within(0.001f));
                }
            }
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, float x, float z,
            SpecialRuleDefinition? rule = null)
        {
            var weapon = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 3; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon }, new Position(x, z + i * 1.2f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: models);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (rule != null) binding.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Routes the movement request to an AI resolver, remembering the request, the reply, and where every model
        // stood when it was asked.
        private sealed class AiMoveRequester : IPlayerRequestByID
        {
            private readonly IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> _resolver;
            public DefineMovementPathRequest? Request;
            public CancellableResult<List<ModelMoveEntry>>? Reply;
            public readonly Dictionary<DataBinding<ModelData>, Position> Starts = new();

            public AiMoveRequester(
                IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> resolver) =>
                _resolver = resolver;

            public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is not DefineMovementPathRequest move)
                    throw new InvalidOperationException("Unexpected request type: " + request.GetType());
                Request = move;
                foreach (DataBinding<ModelData> model in move.UnitDataBinding.GetValue().ModelBindings)
                    Starts[model] = model.GetValue().PositionBinding.GetValue();
                Reply = await _resolver.Resolve(move);
                return (TReply)(object)Reply;
            }
        }

        private sealed class AiStringRequester : IPlayerRequestByID
        {
            private readonly IStageResolver<StringSelectionRequest, string> _resolver;
            public StringSelectionRequest? Asked;
            public string? Reply;

            public AiStringRequester(IStageResolver<StringSelectionRequest, string> resolver) => _resolver = resolver;

            public async Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is not StringSelectionRequest selection)
                    throw new InvalidOperationException("Unexpected request type: " + request.GetType());
                Asked = selection;
                Reply = await _resolver.Resolve(selection);
                return (TReply)(object)Reply;
            }
        }
    }
}
