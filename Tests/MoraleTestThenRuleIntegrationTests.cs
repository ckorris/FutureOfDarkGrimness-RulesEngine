using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197: Effect.MoraleTestThen used to be enacted ONLY by CastSpellStage - its Apply() was a documented
    // no-op - so a plain SpecialRuleDefinition using it was a silent no-op in play. Mind Control (4 refs)
    // and Fatigue Debuff (3) are both ordinary unit rules at the pre-attack hook, so modelling them as
    // spells would not have resolved their corpus references.
    //
    // Owner-signed-off 2026-07-28: make it an ExecutableOperation. Every ability-offering stage already
    // runs OperationExecutor, so the effect now works at all of them rather than only where a stage was
    // taught about it. CastSpellStage keeps its own multi-target path (one aggregated banner, #293) and
    // short-circuits before Apply is called, so nothing double-runs.
    [TestFixture]
    public class MoraleTestThenRuleIntegrationTests
    {
        private const string RuleName = "Fatigue Debuff";

        private GameDataStore _store = null!;
        private PlayerID _owner;
        private PlayerID _victim;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _owner = new PlayerID(System.Guid.NewGuid());
            _victim = new PlayerID(System.Guid.NewGuid());
        }

        private static SpecialRuleDefinition Rule(string name, Effect onFailure) => new(name,
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction,
                    new Cost.OncePerActivation(),
                    new TargetSelector(RangeInches: 18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe,
                        RequireLineOfSight: true),
                    new Effect.MoraleTestThen(onFailure),
                    new Condition.Always(),
                    Label: name),
            });

        [Test]
        public void TheEffectCarriesBothUnits_SoTheConsequenceIsTheOwners()
        {
            // Mind Control's consequence is "you MAY move it" - the rule's owner moves the victim.
            // Effect.TriggeredMove reads Bearer.PlayerID for the controller, so if the operation carried
            // only the victim the enemy would be handed control of its own forced move.
            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));

            var operations = new List<RuleOperation>();
            new Effect.MoraleTestThen(new Effect.TriggeredMove(6f, IsOptional: true)).Apply(
                new RuleInvocation(null, owner.GetValue(), System.Array.Empty<RuleArgument>(),
                    victim.GetValue()),
                operations);

            var op = (RuleOperation.InvokeMoraleTestThen)operations.Single();
            Assert.That(op.Bearer, Is.SameAs(owner.GetValue()), "the rule's owner");
            Assert.That(op.Unit, Is.SameAs(victim.GetValue()), "the unit taking the test");
        }

        [Test]
        public async Task AFailedTest_AppliesTheOnFailureEffect()
        {
            var requester = new CannedRequester();
            // Quality 4 needs a 4+; a die fixed to 1 fails.
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(1));
            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));

            await Execute(ctx, owner, victim, new Effect.ApplyFatigue());

            Assert.That(victim.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "'which must take a morale test. If failed, it becomes fatigued.'");
        }

        [Test]
        public async Task APassedTest_AppliesNothing()
        {
            var requester = new CannedRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(6));
            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));

            await Execute(ctx, owner, victim, new Effect.ApplyFatigue());

            Assert.That(victim.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False,
                "passing the test means no effect at all");
        }

        [Test]
        public async Task AFailedTest_RoutesTheForcedMoveToTheRulesOwner()
        {
            var requester = new CannedRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(1));
            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));

            await Execute(ctx, owner, victim, new Effect.TriggeredMove(6f, IsOptional: true));

            Assert.That(requester.MovePlayer, Is.EqualTo(_owner),
                "'you may move it' - the controlling player is the one who chose to use the rule, " +
                "not the unit being moved");
        }

        // The end-to-end pin: the rule is offered and resolved by the REAL pre-attack stage, which is where
        // both corpus rules live. Before this slice the stage gathered the offer, resolved the ability, and
        // then dropped its no-op effect on the floor.
        [Test]
        public async Task TheRuleResolvesThroughTheRealPreAttackStage()
        {
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            var requester = new CannedRequester { Target = victim };
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(1));

            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            owner.GetValue().AttachRuleDefinition(
                new ResolvedRule(RuleName, Rule(RuleName, new Effect.ApplyFatigue())));

            var unitCtx = new UnitActionContext(ctx, owner);
            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(
                new BeforeAttackActionContext(owner.GetValue()))[0];
            unitCtx.SetPendingCustomAction(offer);

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(victim.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.True,
                "the stage resolved the ability AND executed its operation");
        }

        private static async Task Execute(TriggeredMoveTestContext ctx, DataBinding<UnitData> owner,
            DataBinding<UnitData> victim, Effect onFailure)
        {
            var operations = new List<RuleOperation>
            {
                new RuleOperation.InvokeMoraleTestThen(owner.GetValue(), victim.GetValue(), onFailure),
            };
            await OperationExecutor.Execute(operations, new GameOperationServices(ctx));
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, Position position)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(player, "Test Unit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Answers the target pick (for the stage path) and any forced-move placement, recording which
        // player was asked to make the move - the observable for the controller threading.
        private sealed class CannedRequester : IPlayerRequestByID
        {
            public DataBinding<UnitData>? Target;
            public PlayerID? MovePlayer;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case CancellableSelectionRequest<UnitData>:
                        CancellableResult<DataBinding<UnitData>> pick = Target != null
                            ? new Selected<DataBinding<UnitData>>(Target)
                            : new Cancelled<DataBinding<UnitData>>();
                        return Task.FromResult((TReply)(object)pick);
                    case DefineMovementPathRequest move:
                        // The forced move (Mind Control) asks its controller to draw a path.
                        MovePlayer = move.TargetPlayerID;
                        return Task.FromResult((TReply)(object)
                            new Cancelled<List<ModelMoveEntry>>());
                    case PlaceObjectsRequest<ModelData> place:
                        MovePlayer = place.TargetPlayerID;
                        return Task.FromResult((TReply)(object)
                            new Cancelled<List<PlacedObjectEntry<ModelData>>>());
                    case YesNoRequest yesNo:
                        MovePlayer ??= yesNo.TargetPlayerID;
                        return Task.FromResult((TReply)(object)false);
                    default:
                        throw new System.InvalidOperationException(
                            "Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
