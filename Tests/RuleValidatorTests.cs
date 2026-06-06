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

        // Stealth: DistanceGreaterThan on the hit-modifier hook, whose context provides
        // IHasDistance. Well-formed → no violations.
        [Test]
        public void Validate_StealthOnHitModifierHook_IsClean()
        {
            var stealth = new SpecialRuleDefinition("Stealth",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                NoAbilities);

            Assert.That(_validator.Validate(stealth), Is.Empty);
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
