using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #033 Slice 0 — Caster(X) spell-token economy, driven through the real StartOfRoundExtraActionStage
    // (the same stage that grants tokens in a live game, every round including round 1):
    //  - A Caster(X) unit gains X SpellTokens at the start of a round.
    //  - Unspent tokens carry over between rounds, but the running total is clamped at MAX_SPELL_TOKENS.
    //  - A non-Caster unit gains nothing.
    [TestFixture]
    public class CasterRuleIntegrationTests
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
        public async Task RoundStart_Caster_GrantsRatingTokens()
        {
            DataBinding<UnitData> caster = MakeUnit("Wizards", casterRating: 2);

            await RunRoundStart(roundCount: 1);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2),
                "a Caster(2) unit gains 2 spell tokens at the start of round 1");
        }

        [Test]
        public async Task SpellTokens_CarryOver_ClampedAtMax()
        {
            DataBinding<UnitData> caster = MakeUnit("Wizards", casterRating: 2);

            for (int round = 1; round <= 4; round++)
            {
                await RunRoundStart(round);
            }

            // 2 tokens/round across 4 rounds = 8 uncapped; the grant clamps the carried-over total to the cap.
            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens),
                Is.EqualTo(GameWideConstants.MAX_SPELL_TOKENS),
                "unspent tokens carry over but never exceed the cap");
        }

        [Test]
        public async Task RoundStart_NonCaster_GrantsNoTokens()
        {
            DataBinding<UnitData> plain = MakeUnit("Warriors", casterRating: null);

            await RunRoundStart(roundCount: 1);

            Assert.That(plain.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "a unit without the Caster rule gains no spell tokens");
        }

        // #033 Slice 1 — a damage spell's WithRules are pre-resolved to weapon-scoped ResolvedRules at army
        // load (where the resolver is live), so the cast stage can attach them without a resolver. A plain
        // (arg-less) weapon rule resolves directly.
        [Test]
        public void ResolveSpells_DamageSpell_PreResolvesPlainWeaponRule()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            var armyFile = new ArmyListFile
            {
                Spells = new()
                {
                    new SpellDefinition("Hex", 2,
                        new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                        new Effect.DealHits(2, new[] { "Bane" }, ArmorPenetration: 1)),
                },
            };

            IReadOnlyList<RuntimeSpell> spells = ArmyListSpellResolution.ResolveSpells(armyFile, resolver);

            Assert.That(spells, Has.Count.EqualTo(1));
            Assert.That(spells[0].Threshold, Is.EqualTo(2));
            Assert.That(spells[0].WeaponRules.Select(r => r.Definition.Name), Does.Contain("Bane"),
                "a damage spell's WithRules resolve to weapon-scoped ResolvedRules at load");
        }

        // A numeric weapon rule ("Blast(3)") parses its argument so the resolved rule carries Arg(0)=3.
        [Test]
        public void ResolveSpells_NumericWeaponRule_ParsesArgument()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            var armyFile = new ArmyListFile
            {
                Spells = new()
                {
                    new SpellDefinition("Boom", 1,
                        new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                        new Effect.DealHits(1, new[] { "Blast(3)" })),
                },
            };

            ResolvedRule blast = ArmyListSpellResolution.ResolveSpells(armyFile, resolver).Single().WeaponRules.Single();

            Assert.That(blast.Definition.Name, Is.EqualTo("Blast"));
            Assert.That(((RuleArgument.Int)blast.Arguments[0]).Value, Is.EqualTo(3));
        }

        // #033 Slice 2 — Choose Action offers "Cast" to a caster with an affordable spell and routes it to
        // the cast stage.
        [Test]
        public async Task ChooseAction_CasterWithAffordableSpell_OffersCastAndRoutes()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedStringChoiceRequester("Cast"));
            // Friend-affinity spell so the caster is its own valid target — Cast requires a legal target.
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3,
                new[] { Spell("Zap", threshold: 1, ETargetAffinity.Friend) }, new Position(10f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            bool routed = false;
            var stage = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToCast.Bind("ToCast");
            stage.ToCast.OnWillActivate += _ => routed = true;
            await stage.Enter(unitCtx);

            Assert.That(routed, Is.True, "a caster with a castable spell is offered Cast and routes to the cast stage");
        }

        // #033 Slice 2 — casting spends the spell's token cost (on the attempt) and loops back to Choose
        // Action without consuming the move/attack (layered). TriggeredMoveTestContext's FixedDiceRoller(4)
        // means the 4+ roll passes; the token spend is identical on a failed cast.
        [Test]
        public async Task CastSpellStage_SpendsTokens_AndLoopsBackLayered()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 3,
                new[] { Spell("Zap", threshold: 2, ETargetAffinity.Foe) }, new Position(10f, 10f));
            MakeEnemyUnit(new PlayerID(System.Guid.NewGuid()), new Position(12f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            bool finished = false;
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "casting spends the spell's threshold in tokens (3 - 2)");
            Assert.That(finished, Is.True, "casting loops back to Choose Action");
            Assert.That(unitCtx.HasMoved, Is.False, "casting is layered — it does not consume the move");
            Assert.That(unitCtx.HasAttacked, Is.False, "casting is layered — it does not consume the attack");
        }

        // #033 Slice 3 — a buff spell applies its effect (here AddRule) to the target as a RuleGrant token.
        // The caster is its own (sole) friendly target.
        [Test]
        public async Task CastSpellStage_BuffSpell_GrantsRuleTokenToTarget()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { BuffSpell("Bless", threshold: 1, grantedRule: "Furious") }, new Position(10f, 10f));
            UnitActionContext unitCtx = NewActivation(ctx, caster);

            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);

            Token granted = caster.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant).FirstOrDefault();
            Assert.That(granted, Is.Not.Null, "a buff spell grants the target a RuleGrant token");
            Assert.That(((TokenPayload.RuleGrant)granted!.Payload!).RuleName, Is.EqualTo("Furious"),
                "the RuleGrant token carries the spell's granted rule");
        }

        // #033 Slice 3 — a damage spell runs the synthetic-hit save→wound pipeline against the target. AP(3)
        // pushes the save above 6, so the hit auto-fails and kills the 1-wound enemy; the cast then loops back.
        [Test]
        public async Task CastSpellStage_DamageSpell_AppliesWoundsThroughPipeline()
        {
            var ctx = new TriggeredMoveTestContext(_store, new CannedCastRequester());
            DataBinding<UnitData> caster = MakeCasterUnit(casterRating: 3, tokens: 2,
                new[] { DamageSpell("Bolt", threshold: 2, hits: 1, armorPenetration: 3) }, new Position(10f, 10f));
            DataBinding<UnitData> enemy = MakeEnemyUnit(new PlayerID(System.Guid.NewGuid()), new Position(12f, 10f));

            UnitActionContext unitCtx = NewActivation(ctx, caster);
            bool finished = false;
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            stage.OnFinished.OnWillActivate += _ => finished = true;
            await stage.Enter(unitCtx);

            Assert.That(enemy.GetValue().GetIsAlive(), Is.False,
                "an AP(3) spell hit auto-fails the 1-wound enemy's save and kills it through the real pipeline");
            Assert.That(finished, Is.True, "the damage pipeline returns to Choose Action");
            Assert.That(caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "casting spent the spell's 2-token cost");
        }

        private static RuntimeSpell DamageSpell(string name, int threshold, int hits, int armorPenetration) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.DealHits(hits, System.Array.Empty<string>(), armorPenetration)),
                System.Array.Empty<ResolvedRule>());

        private static RuntimeSpell BuffSpell(string name, int threshold, string grantedRule) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Friend, RequireLineOfSight: false),
                    new Effect.AddRule(grantedRule, ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        // #033 Slice 4 — the spell-menu subtext summarizes a spell's effect and target.
        [Test]
        public void SpellText_Describe_SummarizesEffectAndTarget()
        {
            string damage = SpellText.Describe(new SpellDefinition("Bolt", 2,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                new Effect.DealHits(2, new[] { "Bane" }, ArmorPenetration: 1)));
            Assert.That(damage, Does.Contain("2 hits").And.Contain("AP(1)").And.Contain("Bane")
                .And.Contain("enemy").And.Contain("18"));

            string buff = SpellText.Describe(new SpellDefinition("Bless", 1,
                new TargetSelector(12f, 1, 2, ETargetAffinity.Friend, RequireLineOfSight: false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger)));
            Assert.That(buff, Does.Contain("Furious").And.Contain("up to 2").And.Contain("friendly"));
        }

        private static UnitActionContext NewActivation(IGameContext ctx, DataBinding<UnitData> unit)
        {
            UnitActionContext unitCtx = new UnitActionContext(ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private static RuntimeSpell Spell(string name, int threshold, ETargetAffinity affinity) =>
            new RuntimeSpell(
                new SpellDefinition(name, threshold,
                    new TargetSelector(18f, 1, 1, affinity, RequireLineOfSight: false),
                    new Effect.DealHits(1, System.Array.Empty<string>())),
                System.Array.Empty<ResolvedRule>());

        private DataBinding<UnitData> MakeCasterUnit(int casterRating, int tokens,
            IReadOnlyList<RuntimeSpell> spells, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(_player, "Wizards", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(casterRating) }));
            if (tokens > 0)
            {
                binding.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }

            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { binding });
            army.SetSpells(spells);
            _store.Create(army);
            return binding;
        }

        private DataBinding<UnitData> MakeEnemyUnit(PlayerID enemyPlayer, Position pos)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var modelBindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };

            var unit = new UnitData(enemyPlayer, "Grunts", quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(enemyPlayer, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private async Task RunRoundStart(int roundCount)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NoRequestsRequester());
            var stage = new StartOfRoundExtraActionStage(ctx, new NoOpLayer<IMainPhaseContext>());
            stage.OnFinished.Bind("done");
            await stage.Enter(new TestMainPhaseContext(ctx, roundCount));
        }

        private DataBinding<UnitData> MakeUnit(string name, int? casterRating)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                // Off-origin so the stage's reserve-arrival pass treats the unit as already deployed.
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));

            if (casterRating.HasValue)
            {
                binding.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                    new RuleArgument[] { new RuleArgument.Int(casterRating.Value) }));
            }

            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }

    // The round-start spell-token grant fires no player requests; any request signals a wiring bug.
    internal sealed class NoRequestsRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
            => throw new System.InvalidOperationException(
                "No player request expected during the round-start spell-token grant; got " + request.GetType());
    }

    // Drives CastSpellStage by picking the first offered spell and the first eligible target.
    internal sealed class CannedCastRequester : IPlayerRequestByID
    {
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case StringSelectionRequest spellPick:
                    return Task.FromResult((TReply)(object)spellPick.ValidOptions[0]);
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                default:
                    throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
