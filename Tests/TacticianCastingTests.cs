using System;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 A5 - casting policy: cast whenever the expected value is positive (Cast is layered, so
    // it costs the activation nothing), pick the best spell and the juiciest target, never cancel
    // into the Choose Action loop, and spend assist tokens only when the one-face threshold shift
    // is worth more than the token.
    [TestFixture]
    public class TacticianCastingTests
    {
        private GameDataStore _store = null!;
        private TableState _tableState = null!;
        private RuleEvaluator _evaluator = null!;
        private TacticianPlanner _planner = null!;
        private PlayerID _us;
        private PlayerID _them;
        private Dictionary<PlayerID, List<DataBinding<UnitData>>> _units = null!;

        private static readonly string[] CastableActions =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME, ChooseActionStage.CAST_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _tableState = new TableState(_store);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            _planner = new TacticianPlanner(_tableState, _evaluator);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _units = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
        }

        [Test]
        public void ChooseAction_TakesCast_WhenADamageSpellHasAJuicyTarget()
        {
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            GiveTokens(caster, 3);
            MakeUnit(_them, 5, Blade(), 30f, 24f); // 10" away, inside the 18" spell
            BuildArmies(DamageSpell("Zap", threshold: 2, hits: 6));
            _planner.BeginActivation(caster);

            Assert.That(_planner.ChooseAction(CastableActions),
                Is.EqualTo(ChooseActionStage.CAST_CHOICE_NAME));
        }

        [Test]
        public void ChooseAction_SkipsCast_WhenNoTargetIsInRange()
        {
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            GiveTokens(caster, 3);
            MakeUnit(_them, 5, Blade(), 60f, 24f); // 40" away - far outside the 18" spell
            BuildArmies(DamageSpell("Zap", threshold: 2, hits: 6));
            _planner.BeginActivation(caster);

            Assert.That(_planner.ChooseAction(CastableActions),
                Is.Not.EqualTo(ChooseActionStage.CAST_CHOICE_NAME));
        }

        [Test]
        public void SpellPicker_TakesTheHighestValueSpell_NotTheFirst_AndNeverCancels()
        {
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            GiveTokens(caster, 4);
            MakeUnit(_them, 5, Blade(), 30f, 24f);
            BuildArmies(
                DamageSpell("Spark", threshold: 1, hits: 1),
                DamageSpell("Doom", threshold: 2, hits: 6, ap: 2));
            _planner.BeginActivation(caster);

            string? pick = _planner.ChooseSpell(new[] { "Spark (1)", "Doom (2)", "Cancel" });

            Assert.That(pick, Is.EqualTo("Doom (2)"));
        }

        [Test]
        public void TargetPick_TakesTheJuiciestEnemy_AndNeverCancelsTheFirstPick()
        {
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            GiveTokens(caster, 3);
            var chaff = MakeUnit(_them, 1, Blade(), 26f, 24f);
            var blob = MakeUnit(_them, 5, Blade(), 30f, 24f);
            BuildArmies(DamageSpell("Zap", threshold: 2, hits: 6));
            _planner.BeginActivation(caster);

            bool handled = _planner.TryChooseSpellTarget("Choose target for Zap (1 of up to 1)",
                OptionsFor(chaff, blob), out DataBinding<UnitData>? choice);

            Assert.That(handled, Is.True);
            Assert.That(choice, Is.Not.Null,
                "the first pick never cancels - that would abort the cast unspent and loop Choose Action");
            Assert.That(choice!.Reference.Equals(blob.Reference), Is.True,
                "the bigger unit is the more valuable target");
        }

        [Test]
        public void TargetPick_DeclinesExtraTargets_OnceValueRunsOut()
        {
            // Any-affinity damage spell, up to 3 targets, minimum 1: with the minimum already met,
            // a pick whose best remaining option is a FRIEND (negative value) cancels instead.
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            GiveTokens(caster, 3);
            var friend = MakeUnit(_us, 3, Blade(), 22f, 24f);
            BuildArmies(new RuntimeSpell(new SpellDefinition("Storm", 2,
                new TargetSelector(18f, 1, 3, ETargetAffinity.Any, RequireLineOfSight: false),
                new Effect.DealHits(6, Array.Empty<string>())), Array.Empty<ResolvedRule>()));
            _planner.BeginActivation(caster);

            bool handled = _planner.TryChooseSpellTarget("Choose target for Storm (2 of up to 3)",
                OptionsFor(friend), out DataBinding<UnitData>? choice);

            Assert.That(handled, Is.True);
            Assert.That(choice, Is.Null,
                "damaging our own unit is worse than stopping at the targets already chosen");
        }

        [Test]
        public void Assist_SpendsTokens_OnAValuableCast_AndDeclinesAnUnknownSpell()
        {
            var caster = MakeUnit(_us, 1, Rifle(), 20f, 24f);
            var helper = MakeUnit(_us, 1, Rifle(), 22f, 24f);
            GiveTokens(caster, 3);
            GiveTokens(helper, 3);
            MakeUnit(_them, 5, Blade(), 30f, 24f);
            BuildArmies(DamageSpell("Zap", threshold: 2, hits: 6, ap: 1));
            var resolver = new TacticianCastAssistResolver(_tableState, _evaluator);

            int spent = resolver.Resolve(new CastAssistRequest(_us, helper, caster,
                isFriendly: true, availableTokens: 3, spellName: "Zap")).Result;
            int declined = resolver.Resolve(new CastAssistRequest(_us, helper, caster,
                isFriendly: true, availableTokens: 3, spellName: "NoSuchSpell")).Result;

            Assert.That(spent, Is.GreaterThan(0), "a 1/6 shift on a valuable cast is worth a token");
            Assert.That(spent, Is.LessThanOrEqualTo(TacticianWeights.CastAssistMaxTokens));
            Assert.That(declined, Is.Zero);
        }

        // --- fixtures ---

        private static RuntimeSpell DamageSpell(string name, int threshold, int hits, int ap = 0) =>
            new RuntimeSpell(new SpellDefinition(name, threshold,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                new Effect.DealHits(hits, Array.Empty<string>(), ap)), Array.Empty<ResolvedRule>());

        private static void GiveTokens(DataBinding<UnitData> unit, int count) =>
            unit.GetValue().Tokens.AddToken(new Token(
                TokenType.SpellTokens, count, new TokenClearTrigger.ManualOnly()));

        private static List<SelectionRequest<UnitData>.ValidOption> OptionsFor(
            params DataBinding<UnitData>[] units) =>
            units.Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name)).ToList();

        private static Weapon Rifle() => new Weapon("Rifle", 24f, 1, 0);
        private static Weapon Blade() => new Weapon("Blade", 0f, 2, 0);

        private DataBinding<UnitData> MakeUnit(PlayerID owner, int modelCount, Weapon weapon,
            float atX, float atZ)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { weapon },
                    new Position(atX + (i % 2) * 1.1f, atZ + (i / 2) * 1.1f), _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"U{atX}", quality: 4, defense: 4, modelBindings: modelBindings);
            var binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (!_units.TryGetValue(owner, out List<DataBinding<UnitData>>? list))
                _units[owner] = list = new List<DataBinding<UnitData>>();
            list.Add(binding);
            return binding;
        }

        // One army per player (like a real game), OUR spells attached - created after all units so
        // SpellValuation.ArmyOf's first-match lookup always sees the right army.
        private void BuildArmies(params RuntimeSpell[] ourSpells)
        {
            foreach ((PlayerID owner, List<DataBinding<UnitData>> list) in _units)
            {
                var army = new ArmyData(owner, list);
                if (owner == _us && ourSpells.Length > 0)
                    army.SetSpells(ourSpells.ToList());
                _store.Create(army);
            }
        }
    }
}
