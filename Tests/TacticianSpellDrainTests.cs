using FDG.Ai.Resolvers;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #442 (#429): OPR 3.6 Spell Drain - "Once per activation, before attacking, pick one enemy model within 18" in line
    // of sight, which must take a Morale Test. If failed, it loses 2 spell tokens." The Tactician takes it (a free,
    // layered ability) when an enemy in reach holds tokens, aims it at the most tokens expected to go, never takes it
    // twice in an activation, and its target pick is always one of the offered options (the #066 legality pin).
    [TestFixture]
    public class TacticianSpellDrainTests
    {
        private const string Drain = "Spell Drain";
        private static readonly string[] Options =
        {
            Drain, ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.SHOOT_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private TacticianPlanner _planner = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _planner = new TacticianPlanner(_tableState, _evaluator);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void ChooseAction_TakesSpellDrain_OnAnEnemyCasterHoldingTokens_OnlyOnce()
        {
            DataBinding<UnitData> drainer = Drainer();
            Caster("Witch", tokens: 3, x: 30f);
            _planner.BeginActivation(drainer);

            Assert.That(_planner.ChooseAction(Options), Is.EqualTo(Drain));
            Assert.That(_planner.ChooseAction(Options), Is.Not.EqualTo(Drain),
                "once per activation: the re-entry plans the activation itself");
        }

        [Test]
        public void ChooseAction_SkipsSpellDrain_WhenNoEnemyHoldsTokens()
        {
            DataBinding<UnitData> drainer = Drainer();
            Caster("Witch", tokens: 0, x: 30f);
            _planner.BeginActivation(drainer);

            Assert.That(_planner.ChooseAction(Options), Is.Not.EqualTo(Drain));
        }

        [Test]
        public void ChooseAction_SkipsSpellDrain_OutOfRange()
        {
            DataBinding<UnitData> drainer = Drainer();
            Caster("Witch", tokens: 3, x: 50f); // 30" away, the ability reaches 18"
            _planner.BeginActivation(drainer);

            Assert.That(_planner.ChooseAction(Options), Is.Not.EqualTo(Drain));
        }

        [Test]
        public async Task TargetPick_IsTheMostTokensExpectedToGo_AndAlwaysAnOffer()
        {
            DataBinding<UnitData> drainer = Drainer();
            DataBinding<UnitData> poor = Caster("Apprentice", tokens: 1, x: 28f);
            DataBinding<UnitData> rich = Caster("Archmage", tokens: 3, x: 32f);
            _planner.BeginActivation(drainer);
            Assert.That(_planner.ChooseAction(Options), Is.EqualTo(Drain));

            var resolver = new TacticianBeforeAttackTargetResolver(_planner, new AiChooseMeleeDefenderResolver());
            var request = new CancellableSelectionRequest<UnitData>(_us, "Choose target (1 of up to 1)",
                new List<CancellableSelectionRequest<UnitData>.ValidOption>
                {
                    new(poor, poor.GetValue().Name), new(rich, rich.GetValue().Name),
                },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            CancellableResult<DataBinding<UnitData>> reply = await resolver.Resolve(request);

            Assert.That(reply, Is.InstanceOf<Selected<DataBinding<UnitData>>>(), "never Cancel");
            DataBinding<UnitData> pick = ((Selected<DataBinding<UnitData>>)reply).Value;
            Assert.That(request.ValidOptions.Any(o => o.Option.Reference.Equals(pick.Reference)), Is.True);
            Assert.That(pick.Reference, Is.EqualTo(rich.Reference), "2 of 3 tokens beat 1 of 1");
        }

        [Test]
        public async Task TargetPick_NotTheTacticians_GoesToSolo()
        {
            DataBinding<UnitData> drainer = Drainer();
            DataBinding<UnitData> big = Caster("Big", tokens: 3, x: 30f, models: 3);
            DataBinding<UnitData> small = Caster("Small", tokens: 0, x: 32f, models: 1);
            _planner.BeginActivation(drainer);

            var resolver = new TacticianBeforeAttackTargetResolver(_planner, new AiChooseMeleeDefenderResolver());
            var request = new CancellableSelectionRequest<UnitData>(_us, "Choose target (1 of up to 1)",
                new List<CancellableSelectionRequest<UnitData>.ValidOption> { new(big, "Big"), new(small, "Small") },
                new List<CancellableSelectionRequest<UnitData>.InvalidOption>());

            DataBinding<UnitData> pick = ((Selected<DataBinding<UnitData>>)await resolver.Resolve(request)).Value;
            Assert.That(pick.Reference, Is.EqualTo(small.Reference), "solo's weakest-unit pick, unchanged");
        }

        // --- scene ------------------------------------------------------------------------------------------------

        private static SpecialRuleDefinition SpellDrain() => new(Drain, Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                    new Effect.MoraleTestThen(new Effect.ConsumeToken(TokenType.SpellTokens, 2)),
                    new Condition.Always()),
            },
            ERuleScope.Unit);

        private DataBinding<UnitData> Drainer()
        {
            DataBinding<UnitData> unit = MakeUnit(_us, "Sisters", x: 20f, models: 3);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(Drain, SpellDrain()));
            return unit;
        }

        private DataBinding<UnitData> Caster(string name, int tokens, float x, int models = 1)
        {
            DataBinding<UnitData> unit = MakeUnit(_them, name, x, models);
            if (tokens > 0)
                unit.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens, tokens,
                    new TokenClearTrigger.ManualOnly()));
            return unit;
        }

        private DataBinding<UnitData> MakeUnit(PlayerID owner, string name, float x, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f,
                    new List<Weapon> { new Weapon("Rifle", 24f, 1, 0) }, new Position(x, 24f + i * 1.2f), _store))));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(
                new UnitData(owner, name, quality: 5, defense: 5, modelBindings: bindings)));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
