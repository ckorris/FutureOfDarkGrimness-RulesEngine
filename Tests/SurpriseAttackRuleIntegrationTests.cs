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
    // Vertical-slice integration test for #197 Surprise Attack: "Counts as having Infiltrate. The first time
    // this unit is activated, pick one enemy unit within 6in in line of sight, and roll X dice. For each 2+
    // it takes one hit with AP(1)." The deployment arm is data (the same DeferDeployment passive Infiltrate
    // carries, pinned app-side against the shipped supplement); what this file pins is the burst.
    //
    // Proves: the pool fires by itself at the START of the activation (no menu action, no Yes/No), the
    // successes are HITS that run the real save/wound pipeline with the effect's AP folded in, the count
    // stays FRACTIONAL under the probabilistic roller (the #100 invariant - contrast StormStage, whose pool
    // must be decisive because its successes pick targets), and the once-per-game marker is spent on the
    // first activation whether or not anything was in range (owner ruling 2026-07-30: the burst is lost,
    // not banked). AllOnFaceDiceRoller(f) puts every die on face f, so a pool die is a success at f >= 2 and
    // a save roll succeeds at f >= the needed face.
    [TestFixture]
    public class SurpriseAttackRuleIntegrationTests
    {
        private const string RULE_NAME = "Surprise Attack";
        private static readonly TokenType Used = new("AbilityUsed:" + RULE_NAME);

        private GameDataStore _store = null!;
        private PlayerID _attackerPlayer;
        private PlayerID _defenderPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _attackerPlayer = new PlayerID(Guid.NewGuid());
            _defenderPlayer = new PlayerID(Guid.NewGuid());
        }

        // ── Dispatch: the offer, the argument, and the once-per-game gate ────────────────────────────────

        [Test]
        public void Dispatch_ResolveEmitsThePoolFromTheRulesArgument_AndTheOncePerGameMarker()
        {
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            DataBinding<UnitData> enemy = MakeEnemy("Warriors", new Position(4, 0));

            AbilityOffer offer = ctx.RuleEvaluator
                .GatherOffers(new ActivationStartContext(attacker.GetValue()))
                .Single(o => o.Ability.Effect is Effect.DealPooledHits);
            IReadOnlyList<RuleOperation> ops = ctx.RuleEvaluator.ResolveAbility(offer,
                new[] { (IUnit)enemy.GetValue() });

            RuleOperation.InvokeDealPooledHits pooled =
                ops.OfType<RuleOperation.InvokeDealPooledHits>().Single();
            Assert.That(pooled.Target, Is.SameAs(enemy.GetValue()));
            Assert.That(pooled.DiceCount, Is.EqualTo(5), "Surprise Attack(5) - X comes from the rule's argument");
            Assert.That(pooled.SuccessThreshold, Is.EqualTo(2), "'for each 2+'");
            Assert.That(pooled.ArmorPenetration, Is.EqualTo(1), "'one hit with AP(1)'");
            Assert.That(ops.OfType<RuleOperation.GrantTokenToUnit>().Any(op => op.TokenToGrant.Type == Used),
                Is.True, "the once-per-game marker is queued with the effect");
        }

        [Test]
        public void Dispatch_OncePerGame_NotOfferedOnceTheMarkerIsPresent()
        {
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            attacker.GetValue().Tokens.AddToken(new Token(Used, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(ctx.RuleEvaluator.GatherOffers(new ActivationStartContext(attacker.GetValue()))
                    .Any(o => o.Ability.Effect is Effect.DealPooledHits), Is.False,
                "'the FIRST time this unit is activated' - the marker closes the gate for the rest of the game");
        }

        // ── The stage: the burst lands, with its AP, on the picked enemy ─────────────────────────────────

        [Test]
        public async Task Stage_FirstActivation_EverySuccessIsAHitOnTheEnemyInRange()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 10);

            await RunBurst(ctx, attacker);

            Assert.That(requester.SelectionRequests, Is.EqualTo(0),
                "one eligible enemy - the rule leaves nothing to choose, so no prompt is raised");
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Warriors"), Is.EqualTo(5f),
                "5 dice all showing 2 -> 5 hits, and a 2 fails a 4+ save, so all 5 become wounds");
            Assert.That(attacker.GetValue().Tokens.HasToken(Used), Is.True, "the once-per-game use is spent");
        }

        [Test]
        public async Task Stage_TheBurstsArmorPenetration_Folds_IntoTheSave()
        {
            // Every die is a 4: a pool success (>=2) either way, and against Defense 4+ exactly the roll that
            // saves at AP(0) and fails at AP(1). The control is the same burst with its AP removed - without
            // it this test would pass on a stage that dropped the AP entirely.
            var withAp = new SurpriseRequester();
            var ctxWithAp = new TestGameContext(_store, new AllOnFaceDiceRoller(4), playerRequester: withAp);
            DataBinding<UnitData> piercer = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 10);
            await RunBurst(ctxWithAp, piercer);

            SetUp(); // a fresh table for the control
            var noAp = new SurpriseRequester();
            var ctxNoAp = new TestGameContext(_store, new AllOnFaceDiceRoller(4), playerRequester: noAp);
            DataBinding<UnitData> blunt = MakeAttacker(new Position(0, 0), x: 5, armorPenetration: 0);
            MakeEnemy("Warriors", new Position(4, 0), models: 10);
            await RunBurst(ctxNoAp, blunt);

            Assert.That(withAp.WoundsByUnit.GetValueOrDefault("Warriors"), Is.EqualTo(5f),
                "AP(1) pushes the 4+ save to 5+, so every 4 rolled fails");
            Assert.That(noAp.WoundsByUnit.GetValueOrDefault("Warriors"), Is.EqualTo(0f),
                "control: without the AP the same 4s all save - the AP is what the hits carry");
        }

        [Test]
        public async Task Stage_SeveralEligibleEnemies_MandatoryPickDecidesWhoTakesIt()
        {
            var requester = new SurpriseRequester("Far Squad");
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 3);
            MakeEnemy("Near Squad", new Position(2, 0), models: 10);
            MakeEnemy("Far Squad", new Position(5, 0), models: 10);

            await RunBurst(ctx, attacker);

            Assert.That(requester.SelectionRequests, Is.EqualTo(1), "'pick one enemy unit' - one pick");
            Assert.That(requester.LastAllowedCancel, Is.False,
                "the pick is mandatory: the rule fires on activation and there is nothing to back out to");
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Far Squad"), Is.EqualTo(3f));
            Assert.That(requester.WoundsByUnit.ContainsKey("Near Squad"), Is.False,
                "only the picked unit is hit - this is a single-target burst, not Storm's per-success spread");
        }

        // ── Range, sight, and the "first activation only" ruling ─────────────────────────────────────────

        [Test]
        public async Task Stage_NothingWithinRange_LosesTheBurst_ButStillSpendsTheFirstActivation()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(12, 0), models: 10); // well over 6in

            await RunBurst(ctx, attacker);

            Assert.That(requester.WoundsByUnit, Is.Empty, "no enemy within 6in - nothing is hit");
            Assert.That(attacker.GetValue().Tokens.HasToken(Used), Is.True,
                "owner ruling: 'the FIRST time this unit is activated' - an unusable burst is lost, not banked");
        }

        [Test]
        public async Task Stage_EnemyBehindBlockingTerrain_IsNotEligible()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 10);
            _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(1.5f, 2.5f, -3f, 3f)));

            await RunBurst(ctx, attacker);

            Assert.That(requester.WoundsByUnit, Is.Empty,
                "'in line of sight' - a wall between them makes the only enemy in range ineligible");
        }

        [Test]
        public async Task Stage_SecondActivation_NoSecondBurst()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 20);

            await RunBurst(ctx, attacker);
            float afterFirst = requester.WoundsByUnit.GetValueOrDefault("Warriors");
            await RunBurst(ctx, attacker);

            Assert.That(afterFirst, Is.EqualTo(5f));
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Warriors"), Is.EqualTo(afterFirst),
                "the second activation adds nothing - the burst is once per game");
        }

        // ── The dice invariant ───────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Stage_ProbabilisticMode_TheHitCountStaysFractional()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 30); // survives whatever lands

            await RunBurst(ctx, attacker);

            // 5 dice x 5/6 (a 2+) = 4.1667 hits; against Defense 4+ with AP(1) the save needs a 5+, so 4/6
            // of them fail = 2.7778 wounds. A pool rolled DECISIVELY (StormStage's call, correct there
            // because its successes are target picks) would int-lock this to a whole number of hits.
            Assert.That(requester.WoundsByUnit.GetValueOrDefault("Warriors"), Is.EqualTo(2.7778f).Within(0.01f),
                "the successes are a HIT COUNT, so they stay fractional under the probabilistic roller");
        }

        // ── The seam ActivationStartStage gave up ────────────────────────────────────────────────────────

        [Test]
        public async Task ActivationStart_LeavesTheBurstAlone_SoItIsNotConsumedTwice()
        {
            var requester = new SurpriseRequester();
            var ctx = new TestGameContext(_store, new AllOnFaceDiceRoller(2), playerRequester: requester);
            DataBinding<UnitData> attacker = MakeAttacker(new Position(0, 0), x: 5);
            MakeEnemy("Warriors", new Position(4, 0), models: 10);

            UnitActionContext unitCtx = NewActivation(ctx, attacker);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(unitCtx);

            Assert.That(requester.YesNoRequests, Is.EqualTo(0),
                "the burst is mandatory - ActivationStartStage must not offer it as a 'Use X?' Yes/No");
            Assert.That(attacker.GetValue().Tokens.HasToken(Used), Is.False,
                "and must not spend it: a leaf stage cannot run the save/wound children, so the offer is " +
                "left for SurpriseAttackStage");
            Assert.That(requester.WoundsByUnit, Is.Empty, "nothing was dealt here");
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

        private async Task RunBurst(IGameContext ctx, DataBinding<UnitData> attacker)
        {
            UnitActionContext unitCtx = NewActivation(ctx, attacker);

            bool done = false;
            var stage = new SurpriseAttackStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("done");
            stage.OnFinished.OnWillActivate += _ => done = true;

            await stage.Enter(unitCtx);

            Assert.That(done, Is.True, "the stage always continues to the action menu");
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        // The authored shape of the shipped rule: a once-per-game ability at the activation-start hook whose
        // 6in + line-of-sight ride the TargetSelector and whose pool reads the rule's numeric argument.
        private static SpecialRuleDefinition SurpriseAttackDefinition(int armorPenetration) =>
            new SpecialRuleDefinition(RULE_NAME,
                Array.Empty<HookEntry>(),
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnActivationStart, new Cost.OncePerGame(),
                        new TargetSelector(6f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                        new Effect.DealPooledHits(new ValueSource.Arg(0), SuccessThreshold: 2,
                            ArmorPenetration: armorPenetration),
                        new Condition.Always()),
                },
                Valence: EValence.Positive,
                Description: "The first time this unit is activated, one enemy within 6in takes a pool of hits.");

        private DataBinding<UnitData> MakeAttacker(Position pos, int x, int armorPenetration = 1)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>>
                { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(_attackerPlayer, "Assassin", quality: 4, defense: 4,
                modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule(RULE_NAME,
                SurpriseAttackDefinition(armorPenetration), new RuleArgument[] { new RuleArgument.Int(x) }));
            _store.Create(new ArmyData(_attackerPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private DataBinding<UnitData> MakeEnemy(string name, Position pos, int models = 5)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(pos.x, pos.z + i * 0.01f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(_defenderPlayer, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_defenderPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Every rolled die on a fixed face (Roll(n) yields n dice there).
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

    // Answers the burst's target pick by name (defaulting to the first option), and records the wounds each
    // unit is asked to assign. A YesNo counter guards the ActivationStartStage seam: the burst must never
    // surface as a "Use X?" offer.
    internal sealed class SurpriseRequester : IPlayerRequestByID
    {
        private readonly Queue<string> _pickNames;
        public int SelectionRequests { get; private set; }
        public int YesNoRequests { get; private set; }
        public bool? LastAllowedCancel { get; private set; }
        public Dictionary<string, float> WoundsByUnit { get; } = new();

        public SurpriseRequester(params string[] pickNames) => _pickNames = new Queue<string>(pickNames);

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is SelectionRequest<UnitData> sel)
            {
                SelectionRequests++;
                LastAllowedCancel = sel.AllowCancel;
                string? want = _pickNames.Count > 0 ? _pickNames.Dequeue() : null;
                SelectionRequest<UnitData>.ValidOption option = want != null
                    ? sel.ValidOptions.First(o => o.Name == want)
                    : sel.ValidOptions.First();
                return Task.FromResult((TReply)(object)option.Option);
            }
            if (request is YesNoRequest)
            {
                YesNoRequests++;
                return Task.FromResult((TReply)(object)false);
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
