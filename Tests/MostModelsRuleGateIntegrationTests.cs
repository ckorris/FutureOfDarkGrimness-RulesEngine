using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #428 - OPR 3.6 rewrote every unit-wide gate as "a unit where MOST models have this rule" (core
    // Fearless: "When a unit where most models have this rule fails a Morale Test, roll one die. On a 4+ it
    // counts as passed instead."; Regeneration: "When a unit where most models have this rule takes wounds,
    // roll one die for each. On a 5+ it is ignored."; Stealth: "When units where most models have this rule
    // are shot from over 9" away, enemy units get -1 to hit rolls."). Condition.MostModelsHaveThisRule is a
    // strict majority of the unit's LIVING models, and a joined hero counts as a model of the unit - so a
    // 10-model unit whose hero lacks the rule is 10 of 11 and keeps it (the point of the change; closes #303).
    //
    // Formerly AllModelsRuleGateIntegrationTests (#093/#183), whose host-has/hero-lacks cases are re-pinned
    // here: with a 3-model host that is 3 of 4, so the unit now KEEPS the rule. Exercises the real
    // CoreRuleCatalog definitions through the RuleEvaluator, with the defender's living models threaded the
    // way every Subject-seat stage threads them (HeroStatRules.LivingModels).
    [TestFixture]
    public class MostModelsRuleGateIntegrationTests
    {
        private static readonly Position Pos = new Position(0, 5);

        private GameDataStore _store = null!;
        private RuleEvaluator _evaluator = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _evaluator = new RuleEvaluator(new FixedDiceRoller(1));
        }

        // ---- The majority itself (per-model attachment, so the count can be stated) -------------------

        [TestCase(5, true, TestName = "Stealth_FiveOfNineModels_Qualifies")]
        [TestCase(4, false, TestName = "Stealth_FourOfNineModels_DoesNot")]
        public void Stealth_IsAStrictMajorityOfTheUnitsModels(int carriers, bool applies)
        {
            UnitData defender = MakeUnitWithCarriers(CoreRuleCatalog.Stealth, modelCount: 9, carriers);

            Assert.That(StealthApplies(defender), Is.EqualTo(applies),
                "'units where MOST models have this rule' - more than half of the living models");
        }

        [Test]
        public void Stealth_ExactlyHalf_IsNotMost()
        {
            UnitData defender = MakeUnitWithCarriers(CoreRuleCatalog.Stealth, modelCount: 4, carriers: 2);

            Assert.That(StealthApplies(defender), Is.False, "2 of 4 is half, not most");
        }

        [Test]
        public void Stealth_CasualtiesShiftTheCount_MidGame()
        {
            // 4 carriers of 9: a minority. Carriers are the first four models; non-carriers the last five.
            UnitData defender = MakeUnitWithCarriers(CoreRuleCatalog.Stealth, modelCount: 9, carriers: 4);
            List<IModel> models = defender.Models.ToList();
            Assert.That(StealthApplies(defender), Is.False, "4 of 9 living");

            Kill(models[8]);
            Kill(models[7]);
            Assert.That(StealthApplies(defender), Is.True,
                "two non-carriers fall: 4 of 7 living is most, so the rule now applies");

            Kill(models[0]);
            Assert.That(StealthApplies(defender), Is.False,
                "a carrier falls: 3 of 6 living is half, so it stops applying again");
        }

        // ---- The joined hero is a model of the unit ------------------------------------------------------

        [Test]
        public void Stealth_TenModelUnit_JoinedHeroLacksRule_Qualifies()
        {
            // The case the 3.6 wording exists for: under the old all-models gate one hero without the rule
            // switched it off for ten models that had it.
            UnitData defender = MakeUnitWithHero(CoreRuleCatalog.Stealth, heroHasRule: false, hostModels: 10);

            Assert.That(defender.Models.Count, Is.EqualTo(11), "ten natives plus the joined hero");
            Assert.That(StealthApplies(defender), Is.True, "10 of 11 models have Stealth - most of the unit");
        }

        [Test]
        public void Stealth_HomogeneousUnit_Applies()
        {
            UnitData defender = MakeUnit(modelCount: 5);
            defender.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));

            Assert.That(StealthApplies(defender), Is.True,
                "every model of a plain Stealth unit has the rule, so the -1-to-hit applies.");
        }

        [Test]
        public void Stealth_JoinedHeroLacksRule_StillApplies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);

            Assert.That(StealthApplies(defender), Is.True,
                "3 of 4 living models have Stealth - a joined hero without it no longer breaks it.");
        }

        [Test]
        public void Stealth_JoinedHeroLacksRule_HostDownToOne_Suppressed()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);
            ModelID heroId = defender.JoinedHeroModelId!.Value;
            foreach (IModel grunt in defender.Models.Where(m => m.ID != heroId).Take(2))
            {
                Kill(grunt);
            }

            Assert.That(StealthApplies(defender), Is.False,
                "one grunt and the hero is 1 of 2 - not most, so now the hero lacking it decides it.");
        }

        [Test]
        public void Stealth_OnlyTheJoinedHeroHasIt_Suppressed()
        {
            // The other direction: a hero's own copy is 1 of 4, so it cannot hand the rule to the unit.
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: true, hostHasStealth: false);

            Assert.That(StealthApplies(defender), Is.False, "1 of 4 is a minority");
        }

        [Test]
        public void Stealth_JoinedHeroAlsoHasRule_Applies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: true);

            Assert.That(StealthApplies(defender), Is.True,
                "when the hero also carries Stealth, every living model has it, so the -1 applies.");
        }

        [Test]
        public void Stealth_DeadHeroWithoutRule_AppliesToSurvivors()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false);
            IModel hero = defender.Models.First(m => m.ID == defender.JoinedHeroModelId!.Value);
            Kill(hero); // the hero falls - only native Stealth grunts remain

            Assert.That(StealthApplies(defender), Is.True, "the count ignores dead models: 3 of 3 living.");
        }

        // #183 — unit-held grants cover the joined hero too: a buff cast on the combined unit (or an aura)
        // targets every current model, so the hero counts as a carrier the way it never does for a host
        // STATIC rule.
        [Test]
        public void Stealth_GrantedToUnit_CoversJoinedHero_Applies()
        {
            UnitData defender = MakeStealthUnitWithHero(heroHasStealth: false, hostHasStealth: false);
            GrantStealth(defender);

            var resolver = new RuleResolver();
            resolver.Register(CoreRuleCatalog.Stealth);
            var evaluator = new RuleEvaluator(new FixedDiceRoller(1), null, resolver);

            Assert.That(StealthApplies(defender, evaluator), Is.True,
                "a granted-only Stealth applies to a hero-joined unit: the grant covers every living model.");
        }

        // ---- A unit with no living models is counted as it stood -----------------------------------------

        // Reinforcement fires on "a unit where most models have this rule is ... fully destroyed", which is
        // evaluated on a wiped-out unit. A strict majority of zero living models would be false and kill that
        // arm; the #428 decision is to count the unit's models as they stood, so the hero still can't make a
        // majority on its own there.
        [Test]
        public void ADestroyedUnit_IsCountedAsItStood()
        {
            UnitData homogeneous = MakeUnit(modelCount: 3);
            homogeneous.AttachRuleDefinition(new ResolvedRule("Stealth", CoreRuleCatalog.Stealth));
            homogeneous.Models.ToList().ForEach(Kill);
            Assert.That(GateAnswers(new Condition.MostModelsHaveThisRule(), homogeneous, CoreRuleCatalog.Stealth),
                Is.True, "three dead carriers of three: the destroyed unit had the rule");

            UnitData heroOnly = MakeStealthUnitWithHero(heroHasStealth: true, hostHasStealth: false);
            heroOnly.Models.ToList().ForEach(Kill);
            Assert.That(GateAnswers(new Condition.MostModelsHaveThisRule(), heroOnly, CoreRuleCatalog.Stealth),
                Is.False, "the hero's copy was 1 of 4 when the unit died - still a minority");
        }

        // ---- The strict gate the two 3.6 "all models" holdouts keep --------------------------------------

        [Test]
        public void EveryModelHasThisRule_TenModelUnit_JoinedHeroLacksRule_Fails()
        {
            // Inquisitorial Agent and Martial Prowess still read "if ALL models in this unit have this rule".
            UnitData unit = MakeUnitWithHero(CoreRuleCatalog.MartialProwess, heroHasRule: false, hostModels: 10);

            Assert.That(GateAnswers(new Condition.EveryModelHasThisRule(), unit, CoreRuleCatalog.MartialProwess),
                Is.False, "10 of 11 is most, but not all");
            Assert.That(GateAnswers(new Condition.MostModelsHaveThisRule(), unit, CoreRuleCatalog.MartialProwess),
                Is.True, "...which the most-models gate would have accepted");
        }

        [Test]
        public void TheLegacyAllModelsKind_AnswersLikeMostModels()
        {
            // #428: old army files' "allModelsHaveThisRule" loads as the most-models gate (3.6 semantics).
            UnitData unit = MakeUnitWithHero(CoreRuleCatalog.Stealth, heroHasRule: false, hostModels: 3);

            Assert.That(GateAnswers(new Condition.LegacyAllModelsHaveThisRule(), unit, CoreRuleCatalog.Stealth),
                Is.True, "3 of 4 - the legacy kind no longer means 'every model'");
        }

        // ---- #303: Instinctive with a joined hero that lacks it -----------------------------------------

        [Test]
        public void Instinctive_JoinedHeroWithoutIt_StillCompelsTheMob()
        {
            // #303 asked whether a joined hero without Instinctive should switch it off for the mob. OPR 3.6
            // answers it: the rule binds "a unit where most models have this rule", and three goblins of four
            // models is most. (Shape of the shipped compulsion entry; the rider is irrelevant here.)
            var instinctive = new SpecialRuleDefinition("Instinctive",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.MostModelsHaveThisRule(),
                        new Effect.CompelClosestTarget(), ELifetime.UntilEndOfGame),
                },
                System.Array.Empty<ActivatedAbility>());

            UnitData mob = MakeUnitWithHero(instinctive, heroHasRule: false, hostModels: 3);

            Assert.That(CapabilityRuleQueries.MustAttackClosestSource(mob, _evaluator), Is.EqualTo("Instinctive"));
        }

        // ---- The gate governs every Subject-seat effect class, in both directions ------------------------
        // One case per effect class: hit-mod (Evasive), wound-ignore (Protected), save-mod (Shielded),
        // range-mod (Ranged Shrouding), charge-mod (Melee Shrouding), strike-first (Counter-Attack). Each
        // asserts on op production, which is non-empty iff the rule's gated entry fired.

        [Test]
        public void Evasive_HitModifier_GateGovernsHostAndHero()
        {
            UnitData attacker = MakeUnit(1);
            AssertGate(CoreRuleCatalog.Evasive,
                d => new HitRollModifierContext(attacker, d, DistanceInches: 5f));
        }

        [Test]
        public void Protected_WoundIgnore_GateGovernsHostAndHero()
        {
            UnitData attacker = MakeUnit(1);
            AssertGate(CoreRuleCatalog.Protected,
                d => new SaveRollCompleteContext(attacker, d, TestDice.Faces(1), IsMelee: false));
        }

        [Test]
        public void Shielded_SaveModifier_GateGovernsHostAndHero()
        {
            UnitData attacker = MakeUnit(1);
            AssertGate(CoreRuleCatalog.Shielded,
                d => new HitRollCompleteContext(attacker, d, TestDice.Faces(1), IsSpell: false));
        }

        [Test]
        public void RangedShrouding_RangeModifier_GateGovernsHostAndHero()
        {
            UnitData attacker = MakeUnit(1);
            AssertGate(CoreRuleCatalog.RangedShrouding, _ => new RangeModifierContext(attacker));
        }

        [Test]
        public void MeleeShrouding_ChargeModifier_GateGovernsHostAndHero()
        {
            UnitData charger = MakeUnit(1);
            AssertGate(CoreRuleCatalog.MeleeShrouding,
                d => new ChargeDeclaredContext(charger, d, BaseDistanceInches: 12f));
        }

        [Test]
        public void CounterAttack_StrikeFirst_GateGovernsHostAndHero()
        {
            UnitData attacker = MakeUnit(1);
            AssertGate(CoreRuleCatalog.CounterAttack, d => new CounterTriggerContext(attacker, d));
        }

        // --- harness ---

        // Asserts the most-models gate governs a Subject-seat rule both ways: a homogeneous unit fires it, a
        // 3-model host whose joined hero lacks it still fires it (3 of 4), and a hero-only copy does not
        // (1 of 4). buildContext maps the tested defender to its firing context.
        private void AssertGate(SpecialRuleDefinition rule, System.Func<UnitData, IHookContext> buildContext)
        {
            UnitData homogeneous = MakeUnit(5);
            homogeneous.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            Assert.That(ProducesSubjectOps(homogeneous, buildContext(homogeneous)), Is.True,
                $"a homogeneous {rule.Name} unit: every model has it, so it fires.");

            UnitData heroLacks = MakeUnitWithHero(rule, heroHasRule: false);
            Assert.That(ProducesSubjectOps(heroLacks, buildContext(heroLacks)), Is.True,
                $"a joined hero without {rule.Name} is 1 model of 4, so the unit keeps it.");

            UnitData heroOnly = MakeUnitWithHero(rule, heroHasRule: true, hostHasRule: false);
            Assert.That(ProducesSubjectOps(heroOnly, buildContext(heroOnly)), Is.False,
                $"a hero-only {rule.Name} is 1 of 4 - it cannot fire for the unit.");
        }

        private bool ProducesSubjectOps(UnitData defender, IHookContext context) =>
            _evaluator.EvaluateAll(context,
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender))).Count > 0;

        // True if CoreRuleCatalog.Stealth produces its -1-to-hit modifier for this defender at > 9".
        private bool StealthApplies(UnitData defender, RuleEvaluator? evaluator = null)
        {
            UnitData attacker = MakeUnit(modelCount: 1);
            var context = new HitRollModifierContext(attacker, defender, DistanceInches: 12f);
            IReadOnlyList<RuleOperation> ops = (evaluator ?? _evaluator).EvaluateAll(context,
                RuleParticipant.Actor(attacker),
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));
            return ops.OfType<RuleOperation.ApplyRollModifier>()
                .Any(op => op.Roll == ERollKind.Hit && op.Delta == -1);
        }

        // The bare condition's answer for a unit, as if the given rule were firing on it - for the cases
        // where no live hook would reach the unit (a destroyed unit, an ability's availability).
        private static bool GateAnswers(Condition gate, UnitData unit, SpecialRuleDefinition firing) =>
            gate.Evaluate(new RuleInvocation(new CapabilityQueryContext(unit), unit,
                System.Array.Empty<RuleArgument>(), Definition: firing));

        private static void Kill(IModel model) => model.DealWounds(model.TotalWounds);

        private static void GrantStealth(UnitData unit) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
                Payload: new TokenPayload.RuleGrant("Stealth", ELifetime.UntilEndOfGame)));

        private UnitData MakeStealthUnitWithHero(bool heroHasStealth, bool hostHasStealth = true) =>
            MakeUnitWithHero(CoreRuleCatalog.Stealth, heroHasStealth, hostHasStealth);

        // A host unit carrying <paramref name="rule"/> (statically, when hostHasRule) merged with a 1-model
        // hero that either does or doesn't carry it. Mirrors HeroPerModelRuleIntegrationTests' merge harness.
        private UnitData MakeUnitWithHero(SpecialRuleDefinition rule, bool heroHasRule, bool hostHasRule = true,
            int hostModels = 3)
        {
            UnitData host = MakeUnit(modelCount: hostModels);
            if (hostHasRule)
            {
                host.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            UnitData hero = MakeUnit(modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            if (heroHasRule)
            {
                hero.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            return _store.GetDataBinding<UnitData>(_store.Create(host)).GetValue();
        }

        // A unit whose first <paramref name="carriers"/> models carry the rule on the MODEL - per-model
        // attachment is what lets a test state a count at all (a unit-level rule covers every native model).
        private UnitData MakeUnitWithCarriers(SpecialRuleDefinition rule, int modelCount, int carriers)
        {
            UnitData unit = MakeUnit(modelCount);
            foreach (ModelData model in unit.Models.OfType<ModelData>().Take(carriers))
            {
                model.AttachRuleDefinition(new ResolvedRule(rule.Name, rule));
            }

            return unit;
        }

        private UnitData MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), Pos, _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            return new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
        }
    }
}
