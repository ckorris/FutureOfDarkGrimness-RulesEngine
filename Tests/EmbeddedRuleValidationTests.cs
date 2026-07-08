using System;
using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 workstream 3: embedded rule definitions are validated at load. A capability/hook
    // mismatch (a Condition/Effect needing something its hook's context can't provide) hard-fails
    // the whole army load with a RuleValidationException — nothing registers — rather than
    // silently misbehaving at dispatch. Mirrors RuleValidatorTests' fixtures at the load seam.
    [TestFixture]
    public class EmbeddedRuleValidationTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();

        // Stealth: DistanceGreaterThan on the hit-modifier hook (context provides IHasDistance), And-composed
        // with the #183 AllModelsHaveThisRule gate every Subject-seat defensive rule now requires. Clean.
        private static SpecialRuleDefinition WellFormedRule(string name) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.DistanceGreaterThan(9f),
                            new Condition.AllModelsHaveThisRule()),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack, ERuleSeat.Subject),
                },
                NoAbilities);

        // A distance condition on a lifecycle hook whose context carries no IHasDistance.
        private static SpecialRuleDefinition MisplacedRule(string name) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

        [Test]
        public void WellFormedEmbeddedRule_RegistersWithoutThrowing()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListFile army = new ArmyListFile
            {
                Name = "Valid",
                RuleDefinitions = new() { WellFormedRule("Custom Stealth") },
            };

            Assert.DoesNotThrow(() => ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army));
            Assert.That(resolver.TryResolve("Custom Stealth", out _), Is.True);
        }

        [Test]
        public void InvalidEmbeddedRule_ThrowsAndRegistersNothing()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListFile army = new ArmyListFile
            {
                Name = "Orcs",
                // a valid rule alongside the bad one — the whole load must be rejected, not partially applied.
                RuleDefinitions = new() { WellFormedRule("Custom Stealth"), MisplacedRule("Misplaced") },
            };

            RuleValidationException ex = Assert.Throws<RuleValidationException>(
                () => ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army));

            Assert.That(ex.Violations, Has.Count.EqualTo(1));
            Assert.That(ex.Violations[0].RuleName, Is.EqualTo("Misplaced"));
            Assert.That(ex.Violations[0].MissingCapability, Is.EqualTo(typeof(IHasDistance)));
            Assert.That(ex.Message, Does.Contain("Orcs"));
            Assert.That(ex.Message, Does.Contain("Misplaced"));

            // Validate-all-before-register: the clean rule must NOT have leaked in.
            Assert.That(resolver.TryResolve("Custom Stealth", out _), Is.False,
                "no rule should register when any rule in the army fails validation.");
        }

        [Test]
        public void MultipleViolations_AreAllReported()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListFile army = new ArmyListFile
            {
                Name = "Broken",
                RuleDefinitions = new() { MisplacedRule("BadOne"), MisplacedRule("BadTwo") },
            };

            RuleValidationException ex = Assert.Throws<RuleValidationException>(
                () => ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army));

            Assert.That(ex.Violations, Has.Count.EqualTo(2));
            Assert.That(ex.Message, Does.Contain("BadOne"));
            Assert.That(ex.Message, Does.Contain("BadTwo"));
        }
    }
}
