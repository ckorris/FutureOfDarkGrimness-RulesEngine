using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #435 - the AoF 3.6 Command Groups (AoF Core Rules v3.6.0): "Banner: This model and its unit get +1 to Morale Test
    // rolls triggered by shooting or melee." (3.5: every morale test), "Musician: This model and its unit move +1" when
    // using move actions.", and the books' Great Banner / Great Musician one-shot versions. Banner drives the REAL morale
    // dispatch sites, the HeroMoraleAuraRuleIntegrationTests rig: the wound-driven test (ResolveWoundDrivenMorale, which
    // ResolveRangedMoraleStage and the strafe / Crossing Strike stage call - shooting), the melee loser's
    // (DetermineMoraleSaveNeededStage -> RollForMoraleStage - melee) and a rule-forced one (Spell Drain through
    // BeforeAttackActionStage -> GameOperationServices.MoraleTestThen - neither). The testing unit is Quality 5 and
    // every die shows 4: it fails on its own and passes only with the +1.
    [TestFixture]
    public class AofCommandGroup36RuleIntegrationTests
    {
        private const float BaseRadius = 0.5f;
        private const int Die = 4;
        private const float Origin = 10f;

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private PlayerID _p1;
        private PlayerID _p2;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(Die), ruleResolver: Resolver());
            _p1 = new PlayerID(System.Guid.NewGuid());
            _p2 = new PlayerID(System.Guid.NewGuid());
        }

        // --- Banner: tests triggered by shooting or melee ----------------------------------------------------------

        [Test]
        public async Task Banner_TheWoundDrivenTest_TriggeredByShooting_GetsPlusOne()
        {
            DataBinding<UnitData> warriors = Warriors(AofSweep36Definitions.Banner());

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.False, "Quality 5 -1 = 4+: the 4 passes");
        }

        [Test]
        public async Task Banner_Control_WithoutTheRule_TheSameTestFails()
        {
            DataBinding<UnitData> warriors = Warriors(rule: null);

            await RunWoundDrivenMorale(warriors);

            Assert.That(IsShaken(warriors), Is.True);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public async Task Banner_TheMeleeLosersTest_GetsPlusOne(bool banner, bool expectedPass)
        {
            DataBinding<UnitData> warriors = Warriors(banner ? AofSweep36Definitions.Banner() : null);
            DataBinding<UnitData> chargers = MakeUnit(_p2, "Chargers", models: 2, quality: 4, x: -20f);

            var melee = new CombatActionContext(_ctx, chargers, isMelee: true);
            melee.SetDefender(warriors);
            melee.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));

            var determine = new DetermineMoraleSaveNeededStage(_ctx, new NoOpLayer<ICombatActionContext>());
            determine.ToRollForMorale.Bind(DetermineMoraleSaveNeededStage.DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION);
            await determine.Enter(melee);

            var roll = new RollForMoraleStage(_ctx, new NoOpLayer<ICombatActionContext>());
            bool passed = false;
            roll.OnMoralePassed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_PASSED_TRANSITION);
            roll.OnMoraleFailed.Bind(RollForMoraleStage.ROLL_FOR_MORALE_FAILED_TRANSITION);
            roll.OnMoralePassed.OnWillActivate += _ => passed = true;
            await roll.Enter(melee);

            Assert.That(passed, Is.EqualTo(expectedPass));
        }

        [Test]
        public async Task Banner_ARuleForcedTest_GetsNothing()
        {
            // Spell Drain: "pick one enemy model ..., which must take a Morale Test. If failed, it loses 2 spell tokens."
            // Neither shooting nor melee triggered it, so the Banner-carrying caster tests at its own Quality 5.
            DataBinding<UnitData> victim = Caster(AofSweep36Definitions.Banner());

            await SpellDrain(victim);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "failed at 5+ with no Banner bonus - 2 of 3 tokens drained");
        }

        [Test]
        public async Task Banner_ACallerThatNamesNoCause_GetsNothing()
        {
            // The default cause is Other: a site that predates #435 (or a spell's own Morale Test) never reads as
            // shooting or melee.
            DataBinding<UnitData> warriors = Warriors(AofSweep36Definitions.Banner());

            MoraleUtilities.MoraleTestOutcome outcome =
                await MoraleUtilities.TakeMoraleTest(_ctx, warriors.GetValue(), baseRollNeeded: 5);

            Assert.That(outcome.Passed, Is.False);
            Assert.That(outcome.RollNeeded, Is.EqualTo(5));
        }

        // --- Great Banner: Banner once ----------------------------------------------------------------------------

        [Test]
        public async Task GreatBanner_TheNextShootingTest_GetsPlusOne_ThenTheGrantIsSpent()
        {
            DataBinding<UnitData> warriors = Warriors(rule: null);
            await GreatBanner(warriors);

            await RunWoundDrivenMorale(warriors);
            Assert.That(IsShaken(warriors), Is.False, "the granted Banner's +1");
            Assert.That(GrantCount(warriors), Is.EqualTo(0), "'once (next time the effect would apply)'");

            // A second wound-driven test (the unit is still at half strength and takes another wound) gets nothing.
            await MoraleUtilities.ResolveWoundDrivenMorale(_ctx, warriors, warriors.GetValue().RemainingWounds + 1f);
            Assert.That(IsShaken(warriors), Is.True);
        }

        [Test]
        public async Task GreatBanner_ARuleForcedTestFirst_GetsNothing_AndSpendsTheGrant()
        {
            // #435 D3: the grant is a one-shot Banner, spent by the next morale test whatever triggered it (the #101
            // "the hook fired" occurrence rule) - Banner's own gate keeps the +1 off a test neither shooting nor melee
            // triggered, so a Spell Drain test first costs the grant and helps nothing.
            DataBinding<UnitData> victim = Caster(rule: null);
            await GreatBanner(victim);

            await SpellDrain(victim);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1));
            Assert.That(GrantCount(victim), Is.EqualTo(0));
        }

        // --- Musician / Great Musician ----------------------------------------------------------------------------

        [TestCase(EActionType.Advance)]
        [TestCase(EActionType.Rush)]
        [TestCase(EActionType.Charge)]
        public void Musician_EveryMoveAction_MovesPlusOne_Once(EActionType action)
        {
            DataBinding<UnitData> unit = Warriors(AofSweep36Definitions.Musician());

            var sink = new MovementModifierSink();
            sink.ApplyFrom(_ctx.RuleEvaluator.EvaluateAll(new MoveActionDeclaredContext(unit.GetValue(), action, 6f),
                RuleParticipant.Actor(unit.GetValue())));

            Assert.That(sink.Net(action), Is.EqualTo(1f));
            Assert.That(new[] { EActionType.Advance, EActionType.Rush, EActionType.Charge }
                .Where(a => a != action).Select(a => sink.Net(a)), Is.All.EqualTo(0f),
                "one entry per action: the three-action budget fold never counts it twice");
        }

        [Test]
        public void GreatMusician_GrantsMusicianOnce()
        {
            DataBinding<UnitData> bearer = MakeUnit(_p1, "Standard Bearer", models: 1, quality: 4, x: 3f,
                rule: AofSweep36Definitions.GreatMusician());
            DataBinding<UnitData> friend = Warriors(rule: null);

            AbilityOffer offer = _ctx.RuleEvaluator.GatherOffers(new BeforeAttackActionContext(bearer.GetValue()))
                .Single(o => o.RuleName == "Great Musician");
            OperationApplier.ApplyTokenOperations(_ctx.RuleEvaluator.ResolveAbility(offer, new IUnit[] { friend.GetValue() }));
            Assert.That(friend.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).Single().ClearTrigger,
                Is.InstanceOf<TokenClearTrigger.FirstTrigger>(), "once");

            var sink = new MovementModifierSink();
            sink.ApplyFrom(_ctx.RuleEvaluator.EvaluateAll(
                new MoveActionDeclaredContext(friend.GetValue(), EActionType.Rush, 12f),
                RuleParticipant.Actor(friend.GetValue())));
            Assert.That(sink.Net(EActionType.Rush), Is.EqualTo(1f), "the granted Musician");
            Assert.That(GrantCount(friend), Is.EqualTo(0), "a live evaluation spends the one-shot grant");
        }

        // --- the condition: JSON, validator, lint ------------------------------------------------------------------

        [Test]
        public void TheCondition_RoundTrips_AndTheContextDefaultsToNeitherCause()
        {
            Condition condition = new Condition.MoraleTestFromShootingOrMelee();
            string json = JsonSerializer.Serialize(condition, RuleJson.Options);
            Assert.That(json, Does.Contain("\"moraleTestFromShootingOrMelee\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(condition));

            IUnit unit = Warriors(rule: null).GetValue();
            Assert.That(new PreMoraleTestContext(unit).MoraleTestCause, Is.EqualTo(EMoraleTestCause.Other));
            Assert.That(Evaluate(new PreMoraleTestContext(unit)), Is.False);
            Assert.That(Evaluate(new PreMoraleTestContext(unit, Cause: EMoraleTestCause.Shooting)), Is.True);
            Assert.That(Evaluate(new PreMoraleTestContext(unit, Cause: EMoraleTestCause.Melee)), Is.True);

            bool Evaluate(IHookContext context) => condition.Evaluate(
                new RuleInvocation(context, unit, System.Array.Empty<RuleArgument>()));
        }

        [Test]
        public void TheValidator_AcceptsItAtTheMoraleHook_AndRefusesItWhereNoTestIsTaken()
        {
            Assert.That(new RuleValidator().Validate(AofSweep36Definitions.Banner()), Is.Empty);

            var misplaced = new SpecialRuleDefinition("Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.MoraleTestFromShootingOrMelee(),
                        new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());
            Assert.That(new RuleValidator().Validate(misplaced).Select(v => v.MissingCapability),
                Has.Member(typeof(IHasMoraleTestCause)));
        }

        [Test]
        public void TheFireLint_PassesEveryDefinition()
        {
            foreach (SpecialRuleDefinition rule in new[]
                     {
                         AofSweep36Definitions.Banner(), AofSweep36Definitions.Musician(),
                         AofSweep36Definitions.GreatBanner(), AofSweep36Definitions.GreatMusician(),
                         AofSweep36Definitions.GreatSergeant(),
                     })
            {
                Assert.That(RuleFireLint.Check(rule), Is.Empty, rule.Name);
            }
        }

        // --- helpers -----------------------------------------------------------------------------------------------

        private static RuleResolver Resolver()
        {
            var resolver = new RuleResolver();
            resolver.Register(AofSweep36Definitions.Banner());
            resolver.Register(AofSweep36Definitions.Musician());
            return resolver;
        }

        // The Quality 5 unit under test: four models on the z = 0 line.
        private DataBinding<UnitData> Warriors(SpecialRuleDefinition? rule) =>
            MakeUnit(_p1, "Warriors", models: 4, quality: 5, x: 0f, rule: rule);

        // A Quality 5 single-model caster holding 3 spell tokens, on its own line.
        private DataBinding<UnitData> Caster(SpecialRuleDefinition? rule)
        {
            DataBinding<UnitData> victim = MakeUnit(_p1, "Hex-Witch", models: 1, quality: 5, x: 0f, rule: rule);
            victim.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            victim.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            return victim;
        }

        private async Task GreatBanner(DataBinding<UnitData> friend)
        {
            DataBinding<UnitData> bearer = MakeUnit(_p1, "Standard Bearer", models: 1, quality: 4, x: 3f,
                rule: AofSweep36Definitions.GreatBanner(), z: 2f);
            AbilityOffer offer = _ctx.RuleEvaluator.GatherOffers(new BeforeAttackActionContext(bearer.GetValue()))
                .Single(o => o.RuleName == "Great Banner");
            OperationApplier.ApplyTokenOperations(_ctx.RuleEvaluator.ResolveAbility(offer, new IUnit[] { friend.GetValue() }));
            Assert.That(GrantCount(friend), Is.EqualTo(1));
            await Task.CompletedTask;
        }

        private async Task SpellDrain(DataBinding<UnitData> victim)
        {
            DataBinding<UnitData> drainer = MakeUnit(_p2, "Inquisitor", models: 1, quality: 4, x: -10f,
                rule: SpellDrainRuleIntegrationTests.SpellDrain());
            var ctx = new TestGameContext(_store, new FixedDiceRoller(Die), ruleResolver: Resolver(),
                playerRequester: new PickUnit(victim));

            var unitCtx = new UnitActionContext(ctx, drainer);
            unitCtx.Reset(drainer);
            unitCtx.SetPendingCustomAction(ctx.RuleEvaluator
                .GatherOffers(new BeforeAttackActionContext(drainer.GetValue()))
                .Single(o => o.RuleName == "Spell Drain"));
            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private static int GrantCount(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).Count();

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, int models, int quality, float x,
            SpecialRuleDefinition? rule = null, float z = 0f)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(BaseRadius, new List<Weapon> { new Weapon("Blade", 0f, 1, 0) },
                    new Position(Origin + x + i * 1.5f, Origin + z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(player, name, quality: quality, defense: 4, modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // The two far models fall, leaving the unit at half strength: the shooting test is due.
        private async Task RunWoundDrivenMorale(DataBinding<UnitData> unit)
        {
            float before = unit.GetValue().RemainingWounds;
            for (int i = 2; i < 4; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds);
            }
            await MoraleUtilities.ResolveWoundDrivenMorale(_ctx, unit, before);
        }

        private static bool IsShaken(DataBinding<UnitData> unit) => unit.GetValue().Tokens.HasToken(TokenType.Shaken);

        // Answers the before-attack target pick with the given unit; nothing else is expected.
        private sealed class PickUnit : IPlayerRequestByID
        {
            private readonly DataBinding<UnitData> _target;

            public PickUnit(DataBinding<UnitData> target) => _target = target;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is CancellableSelectionRequest<UnitData>)
                {
                    return Task.FromResult((TReply)(object)new Selected<DataBinding<UnitData>>(_target));
                }

                throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
