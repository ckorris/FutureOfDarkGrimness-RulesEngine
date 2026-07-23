using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #197 P10 Storm of X: "once per game, before attacking, roll 3 dice;
    // for each 2+ pick an enemy within 12in that takes 3 hits with [rule]." Offered in Choose Action and
    // resolved by StormStage. Proves: the pool is rolled DECISIVELY (integer successes even in probabilistic
    // mode - the #100 invariant, since you cannot pick a fractional target), each success independently picks
    // a target, each target's 3 hits run the real save/wound pipeline via the per-batch loop, and the
    // once-per-game gate is spent on use. AllOnFaceDiceRoller(2): every die is a 2 - a pool success (>=2) AND
    // a failed save (<4), so hits convert to wounds; AllOnFaceDiceRoller(1): every die a 1, no successes.
    [TestFixture]
    public class StormRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _attacker;
        private PlayerID _foe;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _attacker = new PlayerID(Guid.NewGuid());
            _foe = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public async Task Stage_ThreeSuccesses_DealsHitsToEachPickedTarget_ThroughTheLoop()
        {
            var requester = new StormRequester("E1", "E2", "E3");
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeStormUnit("Cultists", CoreRuleCatalog.StormOfChange, new Position(0, 0));
            MakeEnemy("E1", new Position(5, 0));
            MakeEnemy("E2", new Position(5, 5));
            MakeEnemy("E3", new Position(5, -5));

            await DriveStorm(ctx, attacker);

            Assert.That(requester.PickRequests, Is.EqualTo(3),
                "3 dice all showing 2 (>=2) -> 3 decisive successes -> 3 independent target picks");
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("E1"), Is.EqualTo(3f), "E1's 3-hit batch all failed saves");
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("E2"), Is.EqualTo(3f), "E2 was picked independently");
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("E3"), Is.EqualTo(3f), "E3 too - per-success distinct targets");
            Assert.That(attacker.GetValue().Tokens.HasToken(UsedMarker("Storm of Change")), Is.True,
                "the once-per-game cost is spent");
        }

        [Test]
        public async Task Stage_ZeroSuccesses_NoHits_ButCostSpent()
        {
            var requester = new StormRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeStormUnit("Cultists", CoreRuleCatalog.StormOfChange, new Position(0, 0));
            MakeEnemy("E1", new Position(5, 0));

            await DriveStorm(ctx, attacker);

            Assert.That(requester.PickRequests, Is.EqualTo(0), "3 dice all showing 1 (<2) -> 0 successes -> no picks");
            Assert.That(requester.WoundsByUnit, Is.Empty, "no hits dealt");
            Assert.That(attacker.GetValue().Tokens.HasToken(UsedMarker("Storm of Change")), Is.True,
                "rolling the storm spends the once-per-game use even on a whiff");
        }

        [Test]
        public async Task Stage_ProbabilisticMode_ResolvesAWholeNumberOfPicks()
        {
            var requester = new StormRequester("E1", "E1", "E1"); // pick E1 for however many successes land
            var ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(),
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);

            DataBinding<UnitData> attacker = MakeStormUnit("Cultists", CoreRuleCatalog.StormOfWar, new Position(0, 0));
            MakeEnemy("E1", new Position(5, 0), models: 20); // survives whatever lands

            await DriveStorm(ctx, attacker);

            // The decisive pool commits each die to a concrete face even under the probabilistic roller, so
            // the success count - the number of target picks - is a whole number in [0,3]. A non-decisive
            // Roll(3).AtOrAbove(2) would give 2.5, which cannot map to a pick count. The loop terminates.
            Assert.That(requester.PickRequests, Is.InRange(0, 3),
                "an integer number of picks - the pool roll never produces a fractional target count");
        }

        [Test]
        public async Task ChooseAction_HasStorm_NotAttacked_RoutesToStorm()
        {
            var requester = new RecordingActionRequester("Storm of Change");
            var ctx = new TriggeredMoveTestContext(_store, requester);
            DataBinding<UnitData> attacker = MakeStormUnit("Cultists", CoreRuleCatalog.StormOfChange, new Position(0, 0));
            var unitCtx = NewActivation(ctx, attacker);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToStorm.Bind("ToStorm");
            stage.ToStorm.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(requester.OfferedOptions, Contains.Item("Storm of Change"));
            Assert.That(routed, Is.True);
            Assert.That(unitCtx.PendingCustomAction?.RuleName, Is.EqualTo("Storm of Change"),
                "the chosen Storm offer is stashed so StormStage can read its config and pay the cost");
        }

        [Test]
        public void GatherOffers_OncePerGameUsed_StormNotOfferedAgain()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> attacker = MakeStormUnit("Cultists", CoreRuleCatalog.StormOfChange, new Position(0, 0));
            attacker.GetValue().Tokens.AddToken(new Token(UsedMarker("Storm of Change"), 1, new TokenClearTrigger.ManualOnly()));

            var offers = ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(attacker.GetValue()));

            Assert.That(offers.Any(o => o.RuleName == "Storm of Change"), Is.False,
                "with the once-per-game marker present the storm is not offered again");
        }

        // --- helpers ---

        private static TokenType UsedMarker(string ruleName) => new("AbilityUsed:" + ruleName);

        // Drives StormStage through its full per-target loop: OnBatchDone (which loops back to the stage in
        // the real graph) is a terminal here, so we re-enter Enter ourselves until OnAllDone fires.
        private async Task DriveStorm(IGameContext ctx, DataBinding<UnitData> attacker)
        {
            var unitCtx = NewActivation(ctx, attacker);
            unitCtx.SetPendingCustomAction(StormOffer(ctx, attacker));

            bool done = false;
            var stage = new StormStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnBatchDone.Bind("batch");
            stage.OnAllDone.Bind("done");
            stage.OnAllDone.OnWillActivate += _ => done = true;

            int safety = 0;
            while (!done && safety++ < 20)
            {
                await stage.Enter(unitCtx);
            }

            Assert.That(done, Is.True, "the per-target loop terminated (queue drained -> OnAllDone)");
        }

        private static AbilityOffer StormOffer(IGameContext ctx, DataBinding<UnitData> unit) =>
            ctx.RuleEvaluator.GatherOffers(new ActionChoiceContext(unit.GetValue()))
                .Single(o => o.Ability.Effect is Effect.StormOfHits);

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private DataBinding<UnitData> MakeStormUnit(string name, SpecialRuleDefinition storm, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(_attacker, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(storm.Name, storm));
            _store.Create(new ArmyData(_attacker, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(string name, Position pos, int models = 5)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(pos.x, pos.z + i * 0.01f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_foe, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_foe, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Returns every rolled die on a fixed face (Roll(n) yields n dice there), and - via the default
        // RollDecisive = Roll(sideCount,1) - a decisive roll of that same face.
        private sealed class AllOnFaceDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public AllOnFaceDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }
    }

    // Picks Storm targets by name (in order) and captures the wound requests each target receives.
    internal sealed class StormRequester : IPlayerRequestByID
    {
        private readonly Queue<string> _pickNames;
        public int PickRequests { get; private set; }
        public Dictionary<string, float> WoundsByUnit { get; } = new();

        public StormRequester(params string[] pickNames) => _pickNames = new Queue<string>(pickNames);

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is CancellableSelectionRequest<UnitData> sel)
            {
                PickRequests++;
                if (_pickNames.Count == 0)
                {
                    return Task.FromResult((TReply)(object)(CancellableResult<DataBinding<UnitData>>)
                        new Cancelled<DataBinding<UnitData>>());
                }
                string want = _pickNames.Dequeue();
                CancellableSelectionRequest<UnitData>.ValidOption opt = sel.ValidOptions.First(o => o.Name == want);
                return Task.FromResult((TReply)(object)(CancellableResult<DataBinding<UnitData>>)
                    new Selected<DataBinding<UnitData>>(opt.Option));
            }
            if (request is AssignWoundsRequest wr)
            {
                string name = wr.UnitReceivingWounds.GetValue().Name;
                WoundsByUnit[name] = WoundsByUnit.GetValueOrDefault(name) + wr.TotalWoundsToAssign;
                var result = new AssignWoundsResults(wr.UnitReceivingWounds, wr.TotalWoundsToAssign);
                result.AutoFill();
                return Task.FromResult((TReply)(object)result);
            }
            throw new InvalidOperationException("Unexpected request: " + request.GetType());
        }
    }
}
