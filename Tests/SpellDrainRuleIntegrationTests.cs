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
using NUnit.Framework;

namespace FDG.Tests
{
    // #429 - Spell Drain, a book rule new in 3.6 (GDF: Blessed Sisters, Daemons of Change, Human Inquisition; AoF:
    // Beastmen, Daemons of Change, Sky-City Dwarves): "Once per activation, before attacking, pick one enemy model
    // within 18" in line of sight, which must take a Morale Test. If failed, it loses 2 spell tokens."
    //
    // No new engine piece: it is the Mind Control / Fatigue Debuff shape (MoraleTestThenRuleIntegrationTests) with
    // Effect.ConsumeToken as the on-failure arm - the morale test runs through GameOperationServices.MoraleTestThen
    // with the failing unit as the effect's target, and OperationApplier drains its SpellTokens (RemoveTokens floors
    // at zero). Spell tokens live on the unit, so "it loses" means the picked model's unit's pool. The definition
    // below is the one authored in GdfRuleSupplement.json (pinned app-side by SpellDrainShippedDataTests); every
    // test drives the REAL BeforeAttackActionStage, where the rule is offered and resolved in play.
    [TestFixture]
    public class SpellDrainRuleIntegrationTests
    {
        private const string RuleName = "Spell Drain";

        private static readonly Position DrainerAt = new Position(0f, 0f);
        private static readonly Position VictimAt = new Position(10f, 0f);

        private GameDataStore _store = null!;
        private PlayerID _owner;
        private PlayerID _victimPlayer;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _owner = new PlayerID(System.Guid.NewGuid());
            _victimPlayer = new PlayerID(System.Guid.NewGuid());
        }

        internal static SpecialRuleDefinition SpellDrain() => new(RuleName,
            System.Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction,
                    new Cost.OncePerActivation(),
                    new TargetSelector(RangeInches: 18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe,
                        RequireLineOfSight: true),
                    new Effect.MoraleTestThen(new Effect.ConsumeToken(TokenType.SpellTokens, 2)),
                    new Condition.Always(),
                    Label: RuleName),
            });

        [Test]
        public async Task AFailedMoraleTest_RemovesTwoSpellTokens()
        {
            // Quality 4 tests on 4+; every die is a 1.
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 3);
            var ctx = Context(victim, face: 1);
            DataBinding<UnitData> drainer = MakeDrainer();

            await UseSpellDrain(ctx, drainer);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1),
                "'If failed, it loses 2 spell tokens' - 3 become 1");
        }

        [Test]
        public async Task AFailedMoraleTest_NeverTakesTheStoreBelowZero()
        {
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 1);
            var ctx = Context(victim, face: 1);
            DataBinding<UnitData> drainer = MakeDrainer();

            await UseSpellDrain(ctx, drainer);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0),
                "one token held, one lost - the pool floors at zero");
        }

        [Test]
        public async Task APassedMoraleTest_RemovesNothing()
        {
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 3);
            var ctx = Context(victim, face: 6);
            DataBinding<UnitData> drainer = MakeDrainer();

            await UseSpellDrain(ctx, drainer);

            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(3),
                "a passed test means no effect at all");
        }

        [Test]
        public async Task AFailedTest_DoesNotShakeTheVictim()
        {
            // A rule's own Morale Test is a test, not a casualty check: failing it costs what the rule says
            // (the tokens) and nothing else - the same as Mind Control and Fatigue Debuff.
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 3);
            var ctx = Context(victim, face: 1);
            DataBinding<UnitData> drainer = MakeDrainer();

            await UseSpellDrain(ctx, drainer);

            Assert.That(victim.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task OncePerActivation_ItIsNotOfferedAgainAfterUse()
        {
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 6);
            var ctx = Context(victim, face: 1);
            DataBinding<UnitData> drainer = MakeDrainer();
            Assert.That(Offers(ctx, drainer), Has.Count.EqualTo(1), "offered before use");

            await UseSpellDrain(ctx, drainer);

            Assert.That(Offers(ctx, drainer), Is.Empty, "'once per activation' - the used marker closes it");
            Assert.That(victim.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(4),
                "and it drained exactly once");
        }

        [Test]
        public void ATargetBehindAWall_IsNotEligible_InLineOfSightIsPartOfTheRule()
        {
            DataBinding<UnitData> victim = MakeCaster(_victimPlayer, VictimAt, tokens: 3);
            var ctx = Context(victim, face: 1);
            DataBinding<UnitData> drainer = MakeDrainer();
            ActivatedAbility ability = SpellDrain().Activated.Single();

            Assert.That(AbilityTargeting.EligibleTargets(drainer, ability.TargetSelector, ctx)
                .Select(b => b.GetValue().Name), Contains.Item("Hex-Witch"), "in sight: eligible");

            _store.Create(new TerrainData(ETerrainType.Blocking, new RectangularZone(4f, 6f, -5f, 5f)));

            Assert.That(AbilityTargeting.EligibleTargets(drainer, ability.TargetSelector, ctx), Is.Empty,
                "'within 18\" in line of sight' - behind the wall it cannot be picked");
        }

        // --- helpers ---------------------------------------------------------------------------------------

        private TriggeredMoveTestContext Context(DataBinding<UnitData> target, int face) =>
            new TriggeredMoveTestContext(_store, new PickTargetRequester(target), new FixedDiceRoller(face));

        private static List<AbilityOffer> Offers(TriggeredMoveTestContext ctx, DataBinding<UnitData> drainer) =>
            ctx.RuleEvaluator.GatherOffers(new BeforeAttackActionContext(drainer.GetValue()))
                .Where(o => o.RuleName == RuleName).ToList();

        private static async Task UseSpellDrain(TriggeredMoveTestContext ctx, DataBinding<UnitData> drainer)
        {
            var unitCtx = new UnitActionContext(ctx, drainer);
            unitCtx.Reset(drainer);
            unitCtx.SetPendingCustomAction(Offers(ctx, drainer).Single());

            var stage = new BeforeAttackActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        private DataBinding<UnitData> MakeDrainer()
        {
            DataBinding<UnitData> drainer = MakeUnit(_owner, "Inquisitor", DrainerAt);
            drainer.GetValue().AttachRuleDefinition(new ResolvedRule(RuleName, SpellDrain()));
            return drainer;
        }

        private DataBinding<UnitData> MakeCaster(PlayerID player, Position at, int tokens)
        {
            DataBinding<UnitData> caster = MakeUnit(player, "Hex-Witch", at);
            caster.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(2) }));
            caster.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, tokens,
                new TokenClearTrigger.ManualOnly()));
            return caster;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            var unit = new UnitData(player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        // Answers the before-attack target pick with the given unit; nothing else is expected.
        private sealed class PickTargetRequester : IPlayerRequestByID
        {
            private readonly DataBinding<UnitData> _target;

            public PickTargetRequester(DataBinding<UnitData> target) => _target = target;

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
