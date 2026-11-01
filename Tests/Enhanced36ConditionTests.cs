using System.Linq;
using System.Text.Json;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #433 - the two primitives the OPR 3.6 "Enhanced X" family needed, against hand-built contexts.
    // (1) Condition.UnmodifiedRollHits: "extra attacks on SUCCESSFUL unmodified hit results of 5-6" - a natural 5 counts
    //     only when it hit (HitRollNeeded on the hit context, the threshold RollToHitStage split the successes with), and
    //     never under Fatigue (#418 strips the 6s; the 5s miss at 6+).
    // (2) Condition.UnitHasRule reads every place a unit can carry a rule - its own list, a RuleGrant token (an aura, a
    //     one-activation pick), a living model (a joined hero), a model's weapon (a re-homed "on its weapons" rule) - so
    //     "If this model has X" is true however X arrived.
    [TestFixture]
    public class Enhanced36ConditionTests
    {
        private TestRuleHarness _harness = null!;
        private IUnit _attacker = null!;
        private IUnit _target = null!;

        [SetUp]
        public void SetUp()
        {
            _harness = new TestRuleHarness();
            _attacker = _harness.BuildUnit("P1", 1);
            _target = _harness.BuildUnit("P2", 1);
        }

        private static bool Holds(Condition condition, IHookContext context, IUnit bearer) =>
            condition.Evaluate(new RuleInvocation(context, bearer, System.Array.Empty<RuleArgument>()));

        private HitRollCompleteContext Hits(int needed, params int[] faces) =>
            new(_attacker, _target, TestDice.Faces(faces), DistanceInches: 10f, HitRollNeeded: needed);

        // --- UnmodifiedRollHits -----------------------------------------------------------------------------------

        [TestCase(2, true)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        [TestCase(6, false)]
        public void ANaturalFive_Counts_OnlyWhenFiveHits(int needed, bool expected)
        {
            Assert.That(Holds(new Condition.UnmodifiedRollHits(5), Hits(needed, 5), _attacker), Is.EqualTo(expected),
                $"a 5 against a {needed}+");
            Assert.That(Holds(new Condition.UnmodifiedRollEquals(5), Hits(needed, 5), _attacker), Is.True,
                "the plain face test does not care whether the 5 hit");
        }

        [Test]
        public void NoThreshold_MeansEveryRolledDieIsAHit()
        {
            // The synthetic-hit pipeline (spells, abilities) and the forecasts build the context without a threshold.
            Assert.That(Holds(new Condition.UnmodifiedRollHits(5), Hits(0, 5), _attacker), Is.True);
            Assert.That(new HitRollCompleteContext(_attacker, _target, TestDice.Faces(5)).HitRollNeeded, Is.EqualTo(0),
                "the default");
        }

        [Test]
        public void TheFace_MustHaveBeenRolled()
        {
            Assert.That(Holds(new Condition.UnmodifiedRollHits(5), Hits(4, 4, 6), _attacker), Is.False);
            Assert.That(Holds(new Condition.UnmodifiedRollHits(5), Hits(4, 4, 5, 6), _attacker), Is.True);
        }

        [Test]
        public void AFatiguedMeleeSwing_GetsNothingFromFivesOrSixes()
        {
            // Fatigue: hits only on 6s (needed 6), and those 6s "don't count as rolls of 6 for special rules" (#418).
            var fatigued = new HitRollCompleteContext(_attacker, _target, TestDice.Faces(5, 6), DistanceInches: 0.5f,
                IsMelee: true, SixesCountForSpecialRules: false, HitRollNeeded: 6);

            Assert.That(Holds(new Condition.UnmodifiedRollHits(5), fatigued, _attacker), Is.False, "the 5 missed");
            Assert.That(Holds(new Condition.UnmodifiedRollHits(6), fatigued, _attacker), Is.False,
                "the 6 hit but does not count for special rules");
            Assert.That(Holds(new Condition.UnmodifiedRollEquals(6), fatigued, _attacker), Is.False,
                "same answer as the base rules' face test");
        }

        [Test]
        public void TheCondition_NamesBothCapabilities_AndThrowsOffAHitRollContext()
        {
            var condition = new Condition.UnmodifiedRollHits(5);

            Assert.That(condition.RequiredCapabilities,
                Is.EquivalentTo(new[] { typeof(IHasUnmodifiedHitRolls), typeof(IHasHitRollNeeded) }));
            Assert.That(() => Holds(condition, new HitRollModifierContext(_attacker, _target, 10f), _attacker),
                Throws.InvalidOperationException, "the hit-modifier hook has no dice yet");
        }

        [Test]
        public void TheValidator_RefusesIt_WhereNoDiceAreRolled()
        {
            var rule = new SpecialRuleDefinition("Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.UnmodifiedRollHits(5),
                        new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());

            Assert.That(new RuleValidator().Validate(rule).Select(v => v.MissingCapability),
                Has.Member(typeof(IHasUnmodifiedHitRolls)).Or.Member(typeof(IHasHitRollNeeded)));

            var placed = new SpecialRuleDefinition("Probe",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.UnmodifiedRollHits(5),
                        new Effect.AddExtraHit(5), ELifetime.ThisAttack),
                },
                System.Array.Empty<ActivatedAbility>());
            Assert.That(new RuleValidator().Validate(placed), Is.Empty, "the hit-complete context carries both");
        }

        [Test]
        public void TheKind_RoundTrips()
        {
            Condition hits = new Condition.UnmodifiedRollHits(5);
            string json = JsonSerializer.Serialize(hits, RuleJson.Options);

            Assert.That(json, Does.Contain("\"kind\":\"unmodifiedRollHits\"").Or.Contain("\"kind\": \"unmodifiedRollHits\""));
            Assert.That(JsonSerializer.Deserialize<Condition>(json, RuleJson.Options), Is.EqualTo(hits));
        }

        // --- UnitHasRule reads every carrier ------------------------------------------------------------------------

        private static readonly SpecialRuleDefinition Stub = new("Lustbound", System.Array.Empty<HookEntry>(),
            System.Array.Empty<ActivatedAbility>());

        private bool UnitHas(IUnit unit, string name = "Lustbound") =>
            Holds(new Condition.UnitHasRule(name), new ActivationStartContext(unit), unit);

        [Test]
        public void OnTheUnitsOwnList_CaseInsensitively()
        {
            IUnit unit = _harness.BuildUnit("P3", 2);
            Assert.That(UnitHas(unit), Is.False, "control: nothing attached");

            _harness.AttachRule(unit, Stub);

            Assert.That(UnitHas(unit), Is.True);
            Assert.That(UnitHas(unit, "lustBOUND"), Is.True);
            Assert.That(UnitHas(unit, "Lustbound Aura"), Is.False, "another name");
        }

        [Test]
        public void ByAnAliasRequestedName()
        {
            IUnit unit = _harness.BuildUnit("P3", 1);
            ((UnitData)unit).AttachRuleDefinition(new ResolvedRule("Agile", Stub));

            Assert.That(UnitHas(unit, "Agile"), Is.True, "the requested (alias) name");
            Assert.That(UnitHas(unit, "Lustbound"), Is.True, "and the canonical one");
        }

        [Test]
        public void AsAGrantToken_TheAuraAndPickShape()
        {
            IUnit unit = _harness.BuildUnit("P3", 2);
            _harness.GrantRule(unit, "Lustbound");

            Assert.That(UnitHas(unit), Is.True, "Lustbound Aura grants Lustbound as a RuleGrant token");
        }

        [Test]
        public void OnALivingModel_TheJoinedHeroShape_ButNotOnlyADeadOne()
        {
            IUnit unit = _harness.BuildUnit("P3", 2);
            ModelData carrier = (ModelData)unit.Models[0];
            carrier.AttachRuleDefinition(new ResolvedRule(Stub.Name, Stub));

            Assert.That(UnitHas(unit), Is.True, "a joined hero's rules are relocated onto its model");

            carrier.DealWounds(1f);
            Assert.That(carrier.GetIsAlive(), Is.False);
            Assert.That(UnitHas(unit), Is.False, "a dead model carries nothing for the unit while others live");
        }

        [Test]
        public void OnAModelsWeapon_TheReHomedWeaponRuleShape()
        {
            // A Weapon-scope rule named at unit level (Infected, Mischievous: "on its weapons") is attached to every
            // weapon and never to the unit list.
            var rifle = new Weapon("Rifle", 24f, 1, 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Infected", new SpecialRuleDefinition("Infected",
                System.Array.Empty<HookEntry>(), System.Array.Empty<ActivatedAbility>(), ERuleScope.Weapon)));
            IUnit unit = _harness.BuildUnit("P3", 1);
            ((ModelData)unit.Models[0]).Weapons.Add(rifle);

            Assert.That(UnitHas(unit, "Infected"), Is.True);
            Assert.That(UnitHas(unit, "Warbound"), Is.False);
        }
    }
}
