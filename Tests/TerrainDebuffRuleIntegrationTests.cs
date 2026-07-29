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
    // #197 P8 - "Dangerous Terrain Debuff" is TWO rules sharing one corpus name. Four books say the victim
    // "counts as being in Dangerous Terrain once (next time the effect would apply)" - a DEFERRED debuff
    // that only bites on the victim's next move, and which #153's Effect.CountAsInTerrain already covers.
    // Lust Disciples and War Disciples say it "must IMMEDIATELY take a Dangerous Terrain test" - it lands
    // on the spot, whether or not the victim ever moves.
    //
    // The distinction is the whole point of this slice: modelling the immediate arm as the deferred one
    // would let a unit that simply holds still shrug the debuff off entirely, inverting the rule. So the
    // immediate arm gets its own effect, and these tests pin BOTH arms against each other - a wrong arm
    // is invisible to --validate-rules and to RuleFireLint, which only ask whether an op is produced.
    [TestFixture]
    public class TerrainDebuffRuleIntegrationTests
    {
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

        // The shipped shape of the immediate arm ("Dangerous Terrain Debuff (Immediate)"), hand-built here
        // because the engine suite cannot read the app's rule supplement. TerrainDebuffShippedDataTests
        // asserts the real authored definition matches this.
        private static SpecialRuleDefinition ImmediateDebuff() => new("Dangerous Terrain Debuff (Immediate)",
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction,
                    new Cost.OncePerActivation(),
                    new TargetSelector(RangeInches: 18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe,
                        RequireLineOfSight: false),
                    new Effect.DangerousTerrainTest(),
                    new Condition.Always()),
            });

        // ... and of the deferred arm's granted rule ("Dangerous Terrain Debuff Effect").
        private static SpecialRuleDefinition DeferredEffect() => new("Dangerous Terrain Debuff Effect",
            new[]
            {
                new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                    new Condition.Always(),
                    new Effect.CountAsInTerrain(ECountAsTerrain.Dangerous),
                    ELifetime.ThisActivation),
            },
            System.Array.Empty<ActivatedAbility>());

        [Test]
        public void TheEffectTargetsTheVICTIM_NotTheBearer()
        {
            // The op carries whoever tests. Reading the bearer here would make the rule shoot its owner in
            // the foot - and every other observable in this file would still pass, because the wounds do
            // land, just on the wrong unit.
            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));

            var operations = new List<RuleOperation>();
            new Effect.DangerousTerrainTest().Apply(
                new RuleInvocation(null, owner.GetValue(), System.Array.Empty<RuleArgument>(),
                    victim.GetValue()),
                operations);

            var op = (RuleOperation.InvokeDangerousTerrainTest)operations.Single();
            Assert.That(op.Unit, Is.SameAs(victim.GetValue()), "the picked enemy takes the test");
        }

        [Test]
        public async Task TheImmediateArm_WoundsAStandingVictim()
        {
            // The clause the deferred arm cannot express: the victim has not moved and never will, and the
            // test still lands. A die fixed to 1 is a wound.
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(), new FixedDiceRoller(1));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            float before = victim.GetValue().Models[0].WoundsDealt;

            await ForceTest(ctx, victim);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before + 1f),
                "'must immediately take a Dangerous Terrain test' - a 1 is a wound.");
        }

        [Test]
        public async Task TheImmediateArm_RollOf2_DealsNoWound()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(), new FixedDiceRoller(2));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            float before = victim.GetValue().Models[0].WoundsDealt;

            await ForceTest(ctx, victim);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before),
                "only a 1 wounds - the test uses the same threshold a real crossing does.");
        }

        [Test]
        public async Task TheImmediateArm_TestsEveryLivingModel_AndNoDeadOnes()
        {
            // "each of its models" is per-model, not per-unit: a five-model unit takes five dice, not one.
            // Dead models are already off the table and must not draw a die - which would both inflate the
            // batch and hand wounds to corpses.
            // FixedFaceDiceRoller, not FixedDiceRoller: the latter reports one die however many were
            // rolled, which would hide exactly the batch-size error this test is for.
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(),
                new FixedFaceDiceRoller(1));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f), modelCount: 3);
            ModelData corpse = (ModelData)victim.GetValue().Models[2];
            corpse.DealWounds(corpse.TotalWounds);

            // ModelCount is the batch size, and it is the only place the corpse is observable: a wound
            // handed to an already-dead model clamps at its total, so the wound tallies alone would stay
            // green with the dead filter removed.
            MovementExecutor.DangerousTerrainResult roll =
                MovementExecutor.RollForcedDangerousTerrain(ctx, victim.GetValue());
            Assert.That(roll.ModelCount, Is.EqualTo(2), "two living models, two dice - the corpse rolls none");

            await ForceTest(ctx, victim);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(1f));
            Assert.That(victim.GetValue().Models[1].WoundsDealt, Is.EqualTo(1f));
        }

        [Test]
        public async Task AFlyingVictim_TakesNoTestAtAll()
        {
            // Owner-ruled 2026-07-28: one rule across all three dangerous-terrain paths. Flying already
            // waives the real crossing and the counts-as grant, so it waives the forced test too.
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(), new FixedDiceRoller(1));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            victim.GetValue().AttachRuleDefinition(new ResolvedRule("Flying", CoreRuleCatalog.Flying));
            float before = victim.GetValue().Models[0].WoundsDealt;

            await ForceTest(ctx, victim);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before),
                "Flying ignores all terrain, including a test forced on it.");
        }

        [Test]
        public async Task AStriderVictim_StillTakesTheTest()
        {
            // Strider waives the DIFFICULT-terrain cap only, so it is wounded by dangerous terrain exactly
            // as it would be walking through the real thing. The Flying waiver above must not widen to it.
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(), new FixedDiceRoller(1));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            victim.GetValue().AttachRuleDefinition(new ResolvedRule("Strider", CoreRuleCatalog.Strider));
            float before = victim.GetValue().Models[0].WoundsDealt;

            await ForceTest(ctx, victim);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before + 1f),
                "Strider is DifficultOnly - it never waived dangerous terrain.");
        }

        // The end-to-end pin: offered and resolved by the REAL pre-attack stage, which is where the corpus
        // rule lives. An effect the stage never executes is the #196 Breath Attack failure mode.
        [Test]
        public async Task TheImmediateArm_ResolvesThroughTheRealPreAttackStage()
        {
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            var requester = new CannedRequester { Target = victim };
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(1));

            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            owner.GetValue().AttachRuleDefinition(
                new ResolvedRule("Dangerous Terrain Debuff (Immediate)", ImmediateDebuff()));
            float before = victim.GetValue().Models[0].WoundsDealt;

            var unitCtx = new UnitActionContext(ctx, owner);
            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(
                new BeforeAttackActionContext(owner.GetValue())).Single();
            unitCtx.SetPendingCustomAction(offer);

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before + 1f),
                "the stage resolved the ability AND executed its operation");
        }

        // The two arms, side by side. This is the test that would go red if the immediate arm were ever
        // "simplified" into the deferred one.
        [Test]
        public async Task TheDeferredArm_ArmsTheNextMoveInstead_AndWoundsNothingNow()
        {
            var resolver = CoreRuleCatalog.CreateResolver();
            resolver.Register(DeferredEffect());
            var ctx = new TriggeredMoveTestContext(_store, new CannedRequester(), new FixedDiceRoller(1),
                ruleResolver: resolver);

            DataBinding<UnitData> owner = MakeUnit(_owner, new Position(0f, 0f));
            DataBinding<UnitData> victim = MakeUnit(_victim, new Position(6f, 0f));
            float before = victim.GetValue().Models[0].WoundsDealt;

            var operations = new List<RuleOperation>();
            new Effect.AddRule("Dangerous Terrain Debuff Effect", ELifetime.NextTrigger).Apply(
                new RuleInvocation(null, owner.GetValue(), System.Array.Empty<RuleArgument>(),
                    victim.GetValue()),
                operations);
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(ctx));

            Assert.That(victim.GetValue().Models[0].WoundsDealt, Is.EqualTo(before),
                "the deferred arm wounds nothing at grant time - it only arms the victim's next move.");
            Assert.That(MovementRuleQueries.CountsAsInTerrain(victim.GetValue(), ctx.RuleEvaluator,
                    ECountAsTerrain.Dangerous), Is.True,
                "...and the victim now counts as standing in Dangerous Terrain for that move.");
        }

        private static async Task ForceTest(TriggeredMoveTestContext ctx, DataBinding<UnitData> victim)
        {
            var operations = new List<RuleOperation>
            {
                new RuleOperation.InvokeDangerousTerrainTest(victim.GetValue()),
            };
            await OperationExecutor.Execute(operations, new GameOperationServices(ctx));
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, Position position, int modelCount = 1)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(position.x + i, position.z), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, "Test Unit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Answers the pre-attack stage's target pick; nothing else in this slice asks anything.
        private sealed class CannedRequester : IPlayerRequestByID
        {
            public DataBinding<UnitData>? Target;

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
                    default:
                        throw new System.InvalidOperationException(
                            "Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
