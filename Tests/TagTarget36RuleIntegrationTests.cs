using System;
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
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #434 - OPR 3.6 rewrote the Tag / Target pair:
    //  * Piercing / Precision Tag(X): "Once per game, during this model's activation, before attacking, place X tokens on one
    //    enemy unit within 24" and in line of sight, or one token on X enemy units within 24" and in line of sight. When
    //    attacking, friendly units may remove ONE token from their target before rolling to block / to hit to get AP(+1) /
    //    +1 to hit." (3.5: X markers on one unit, remove any number for +1 each.)
    //  * Piercing / Precision Target(X): "Once per game, ..., pick X enemy units within 18" and in line of sight of it, and
    //    place one token on them. Friendly units get AP(+1) / +1 to hit rolls when attacking them." (3.5: X markers on one
    //    unit, +1 per marker.)
    // New vocabulary: TargetSelector.MaxCountFromArgument (pick up to X units), ValueSource.ArgIfSoleTarget (X tokens on a
    // lone target, 1 each when spread, read off RuleInvocation.TargetCount), and the single-spend Tag tokens TargetMarkerSpend
    // claims one per attack. Driven through the real BeforeAttackActionStage (the target loop) and TargetMarkerSpend.
    [TestFixture]
    public class TagTarget36RuleIntegrationTests
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

        internal static SpecialRuleDefinition Tag(string name, TokenType token) => new(name,
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerGame(),
                    new TargetSelector(24f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true,
                        MaxCountFromArgument: true),
                    new Effect.GrantToken(token, new ValueSource.ArgIfSoleTarget(0), new TokenClearTrigger.ManualOnly()),
                    new Condition.Always()),
            },
            ERuleScope.Unit, EngineArgumentCount: 1);

        internal static SpecialRuleDefinition Target(string name, TokenType token) => new(name,
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerGame(),
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true,
                        MaxCountFromArgument: true),
                    new Effect.GrantToken(token, new ValueSource.Literal(1), new TokenClearTrigger.ManualOnly()),
                    new Condition.Always()),
            },
            ERuleScope.Unit, EngineArgumentCount: 1);

        // --- placing -------------------------------------------------------------------------------------------

        [Test]
        public async Task PiercingTag_OnOneUnit_PlacesXTokens()
        {
            (DataBinding<UnitData> tagger, List<DataBinding<UnitData>> enemies) = Board(enemyCount: 3);
            var requester = new PickRequester(picks: 1);

            await UseBeforeAttack(tagger, Tag("Piercing Tag", TokenType.SingleSpendApBonus), x: 2, requester);

            Assert.That(enemies[0].GetValue().Tokens.GetTokenCount(TokenType.SingleSpendApBonus), Is.EqualTo(2));
            Assert.That(enemies.Skip(1).All(e => !e.GetValue().Tokens.HasToken(TokenType.SingleSpendApBonus)), Is.True);
        }

        [Test]
        public async Task PiercingTag_SpreadOverXUnits_PlacesOneTokenOnEach_AndAsksForAtMostX()
        {
            (DataBinding<UnitData> tagger, List<DataBinding<UnitData>> enemies) = Board(enemyCount: 3);
            var requester = new PickRequester(picks: 5);

            await UseBeforeAttack(tagger, Tag("Piercing Tag", TokenType.SingleSpendApBonus), x: 2, requester);

            Assert.That(requester.Asked, Is.EqualTo(2), "'one token on X enemy units' - at most X picks");
            Assert.That(enemies.Take(2).Select(e => e.GetValue().Tokens.GetTokenCount(TokenType.SingleSpendApBonus)),
                Is.EqualTo(new[] { 1, 1 }));
            Assert.That(enemies[2].GetValue().Tokens.HasToken(TokenType.SingleSpendApBonus), Is.False);
        }

        [Test]
        public async Task PrecisionTarget_PicksXUnits_OneTokenEach()
        {
            (DataBinding<UnitData> tagger, List<DataBinding<UnitData>> enemies) = Board(enemyCount: 3);
            var requester = new PickRequester(picks: 5);

            await UseBeforeAttack(tagger, Target("Precision Target", TokenType.PersistentHitBonus), x: 2, requester);

            Assert.That(requester.Asked, Is.EqualTo(2));
            Assert.That(enemies.Take(2).Select(e => e.GetValue().Tokens.GetTokenCount(TokenType.PersistentHitBonus)),
                Is.EqualTo(new[] { 1, 1 }), "one token on each, not X on one");
        }

        [Test]
        public async Task ASelectorWithoutTheFlag_KeepsItsMaxCount()
        {
            (DataBinding<UnitData> tagger, _) = Board(enemyCount: 3);
            var requester = new PickRequester(picks: 5);
            var plain = new SpecialRuleDefinition("Plain Tag", Array.Empty<HookEntry>(), new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerGame(),
                    new TargetSelector(24f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                    new Effect.GrantToken(TokenType.SpendableApBonus, new ValueSource.Arg(0),
                        new TokenClearTrigger.ManualOnly()),
                    new Condition.Always()),
            }, ERuleScope.Unit, EngineArgumentCount: 1);

            await UseBeforeAttack(tagger, plain, x: 2, requester);

            Assert.That(requester.Asked, Is.EqualTo(1));
        }

        // --- spending ------------------------------------------------------------------------------------------

        [Test]
        public async Task ATagToken_IsSpentOnePerAttack_ForPlusOne()
        {
            IUnit defender = MakeUnit(_them, 10f).GetValue();
            defender.Tokens.AddToken(new Token(TokenType.SingleSpendApBonus, 2, new TokenClearTrigger.ManualOnly()));
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: new SpendRequester(spend: true));

            int net = await TargetMarkerSpend.ConsumeNet(ctx, _us, defender, ERollKind.Save);

            Assert.That(net, Is.EqualTo(1), "'remove ONE token ... to get AP(+1)'");
            Assert.That(defender.Tokens.GetTokenCount(TokenType.SingleSpendApBonus), Is.EqualTo(1), "one removed");
        }

        [Test]
        public async Task DecliningTheTag_KeepsTheToken()
        {
            IUnit defender = MakeUnit(_them, 10f).GetValue();
            defender.Tokens.AddToken(new Token(TokenType.SingleSpendHitBonus, 1, new TokenClearTrigger.ManualOnly()));
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), playerRequester: new SpendRequester(spend: false));

            Assert.That(await TargetMarkerSpend.ConsumeNet(ctx, _us, defender, ERollKind.Hit), Is.EqualTo(0));
            Assert.That(defender.Tokens.GetTokenCount(TokenType.SingleSpendHitBonus), Is.EqualTo(1));
        }

        // --- vocabulary ----------------------------------------------------------------------------------------

        [Test]
        public void TheVocabulary_RoundTripsThroughJson_AndOldSelectorsParseWithoutTheFlag()
        {
            var selector = new TargetSelector(24f, 1, 1, ETargetAffinity.Foe, true, MaxCountFromArgument: true);
            string json = JsonSerializer.Serialize(selector, RuleJson.Options);
            Assert.That(JsonSerializer.Deserialize<TargetSelector>(json, RuleJson.Options), Is.EqualTo(selector));
            Assert.That(JsonSerializer.Deserialize<TargetSelector>(
                    "{\"rangeInches\":24,\"minCount\":1,\"maxCount\":1,\"targetAffinity\":\"Foe\",\"requireLineOfSight\":true}",
                    RuleJson.Options)!.MaxCountFromArgument, Is.False);

            ValueSource sole = new ValueSource.ArgIfSoleTarget(0);
            string soleJson = JsonSerializer.Serialize(sole, RuleJson.Options);
            Assert.That(soleJson, Does.Contain("argIfSoleTarget"));
            Assert.That(JsonSerializer.Deserialize<ValueSource>(soleJson, RuleJson.Options), Is.EqualTo(sole));
        }

        [TestCase(1, 3)]
        [TestCase(2, 1)]
        public void ArgIfSoleTarget_IsXForALoneTarget_OneWhenSpread(int targetCount, int expected)
        {
            IUnit unit = MakeUnit(_us, 0f).GetValue();
            var invocation = new RuleInvocation(null, unit, new RuleArgument[] { new RuleArgument.Int(3) },
                TargetCount: targetCount);

            Assert.That(new ValueSource.ArgIfSoleTarget(0).Resolve(invocation), Is.EqualTo(expected));
        }

        [Test]
        public void TheTagRules_DeclareTheirArgument()
        {
            Assert.That(RuleArgumentArity.MaxReferencedArgIndex(Tag("Piercing Tag", TokenType.SingleSpendApBonus)),
                Is.EqualTo(0));
            Assert.That(RuleFireLint.Check(Tag("Piercing Tag", TokenType.SingleSpendApBonus)), Is.Empty);
            Assert.That(RuleFireLint.Check(Target("Piercing Target", TokenType.PersistentApBonus)), Is.Empty);
        }

        // --- helpers --------------------------------------------------------------------------------------------

        private (DataBinding<UnitData> Tagger, List<DataBinding<UnitData>> Enemies) Board(int enemyCount)
        {
            DataBinding<UnitData> tagger = MakeUnit(_us, 0f);
            var enemies = Enumerable.Range(0, enemyCount).Select(i => MakeUnit(_them, 6f + i * 3f)).ToList();
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { tagger }));
            _store.Create(new ArmyData(_them, enemies));
            return (tagger, enemies);
        }

        private async Task UseBeforeAttack(DataBinding<UnitData> bearer, SpecialRuleDefinition rule, int x,
            IPlayerRequestByID requester)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(4));
            bearer.GetValue().AttachRuleDefinition(new ResolvedRule(rule.Name, rule,
                new RuleArgument[] { new RuleArgument.Int(x) }));

            var unitCtx = new UnitActionContext(ctx, bearer);
            AbilityOffer offer = ctx.RuleEvaluator.GatherOffers(new BeforeAttackActionContext(bearer.GetValue()))
                .Single(o => o.RuleName == rule.Name);
            unitCtx.SetPendingCustomAction(offer);

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, float x)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, 10f), _store);
            var unit = new UnitData(player, $"Unit {x}", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        // Picks the first eligible target up to `picks` times, then cancels.
        private sealed class PickRequester : IPlayerRequestByID
        {
            private readonly int _picks;
            public int Asked;
            public PickRequester(int picks) => _picks = picks;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is CancellableSelectionRequest<UnitData> pick)
                {
                    Asked++;
                    CancellableResult<DataBinding<UnitData>> answer = Asked <= _picks
                        ? new Selected<DataBinding<UnitData>>(pick.ValidOptions[0].Option)
                        : new Cancelled<DataBinding<UnitData>>();
                    return Task.FromResult((TReply)(object)answer);
                }

                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }

        private sealed class SpendRequester : IPlayerRequestByID
        {
            private readonly bool _spend;
            public SpendRequester(bool spend) => _spend = spend;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest menu)
                    return Task.FromResult((TReply)(object)(_spend ? menu.ValidOptions[0] : menu.ValidOptions[^1]));
                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
