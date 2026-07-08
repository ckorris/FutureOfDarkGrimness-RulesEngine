using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // Exercises RuleValidator against the live HookContextCatalog (reflected from the
    // real contexts). Confirms a well-authored rule is clean, and that a condition
    // requiring a capability its hook's context lacks is reported.
    [TestFixture]
    public class RuleValidatorTests
    {
        private static readonly ActivatedAbility[] NoAbilities = Array.Empty<ActivatedAbility>();
        private readonly RuleValidator _validator = new();

        // Stealth: DistanceGreaterThan on the hit-modifier hook, whose context provides IHasDistance,
        // And-composed with the #183 AllModelsHaveThisRule gate the Subject seat now requires. Well-formed
        // → no violations.
        [Test]
        public void Validate_StealthOnHitModifierHook_IsClean()
        {
            var stealth = new SpecialRuleDefinition("Stealth",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.DistanceGreaterThan(9f),
                            new Condition.AllModelsHaveThisRule()),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                NoAbilities);

            Assert.That(_validator.Validate(stealth), Is.Empty);
        }

        // #183 — a unit-scoped rule with a Subject-seat entry at a defensive attack-interaction hook must
        // gate on AllModelsHaveThisRule; an ungated one is reported so the design can't be silently
        // reintroduced by a future author (catalog and embedded/imported supplement both flow through here).
        [Test]
        public void Validate_UngatedSubjectDefensiveRule_ReportsGateViolation()
        {
            var ungated = new SpecialRuleDefinition("Naive Evasive",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                NoAbilities);

            var violations = _validator.Validate(ungated);

            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].Member, Is.EqualTo("Condition"));
            Assert.That(violations[0].MissingCapability, Is.Null);
            Assert.That(violations[0].Describe(), Does.Contain("AllModelsHaveThisRule"));
        }

        // Weapon-scoped rules ride the hero's weapon and survive the merge, so the all-models gate is
        // meaningless for them (no unit-wide projection to guard) — Counter is exempt despite its
        // Subject-seat StrikeFirst entry.
        [Test]
        public void Validate_WeaponScopedSubjectRule_IsExemptFromGate()
        {
            var weaponRule = new SpecialRuleDefinition("Counter-ish",
                new[]
                {
                    new HookEntry(EHookID.Melee_OnCounterTrigger,
                        new Condition.Always(),
                        new Effect.StrikeFirst(),
                        ELifetime.ThisActivation, ERuleSeat.Subject),
                },
                NoAbilities,
                ERuleScope.Weapon);

            Assert.That(_validator.Validate(weaponRule), Is.Empty);
        }

        // An Actor-seat entry at the same hook is the attacker's own buff, not a "unit benefits" defensive
        // rule, so the gate does not apply.
        [Test]
        public void Validate_ActorSeatEntry_IsExemptFromGate()
        {
            var actorRule = new SpecialRuleDefinition("Precise-ish",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

            Assert.That(_validator.Validate(actorRule), Is.Empty);
        }

        // Furious: condition AND effect both require IHasUnmodifiedHitRolls, which the
        // hit-complete context provides. Clean.
        [Test]
        public void Validate_FuriousOnHitCompleteHook_IsClean()
        {
            var furious = new SpecialRuleDefinition("Furious",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

            Assert.That(_validator.Validate(furious), Is.Empty);
        }

        // A distance condition authored on a hook whose context (UnitCreated) carries no
        // capability — the kind of mistake the authoring tool must catch before save.
        [Test]
        public void Validate_DistanceConditionOnCapabilitylessHook_ReportsViolation()
        {
            var misplaced = new SpecialRuleDefinition("Misplaced",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

            var violations = _validator.Validate(misplaced);

            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].Member, Is.EqualTo("Condition"));
            Assert.That(violations[0].MissingCapability, Is.EqualTo(typeof(IHasDistance)));
        }

        // A rule that reads nothing from the context (Always + RollModifier) is valid on
        // any hook, even a capability-less one — empty requirements never flag.
        [Test]
        public void Validate_NoCapabilityRequirements_IsCleanOnAnyHook()
        {
            var harmless = new SpecialRuleDefinition("Harmless",
                new[]
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

            Assert.That(_validator.Validate(harmless), Is.Empty);
        }
    }
}
