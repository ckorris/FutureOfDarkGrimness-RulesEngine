using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 registration slice: an army's embedded RuleDefinitions register into the game's resolver
    // (core-first, then override-by-name) before its units resolve rule names. Proves the override and
    // additive paths at the resolver level — the behavior FDGServer wires up via RegisterEmbeddedDefinitions.
    [TestFixture]
    public class EmbeddedRuleRegistrationTests
    {
        private static SpecialRuleDefinition NamedRule(string name) =>
            new SpecialRuleDefinition(name, new List<HookEntry>(), new List<ActivatedAbility>());

        [Test]
        public void EmbeddedDefinition_OverridesCoreRuleByName()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            Assert.That(resolver.TryResolve("Stealth", out ResolvedRule core), Is.True);
            Assert.That(core.Definition, Is.SameAs(CoreRuleCatalog.Stealth), "precondition: core Stealth registered.");

            SpecialRuleDefinition overrideStealth = NamedRule("Stealth"); // a different instance, same name
            ArmyListFile army = new ArmyListFile { RuleDefinitions = new() { overrideStealth } };

            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);

            Assert.That(resolver.TryResolve("Stealth", out ResolvedRule after), Is.True);
            Assert.That(after.Definition, Is.SameAs(overrideStealth),
                "the army's embedded definition must override the core rule of the same name.");
            Assert.That(after.Definition, Is.Not.SameAs(CoreRuleCatalog.Stealth));
        }

        [Test]
        public void EmbeddedDefinition_WithNewName_IsAdditive_AndLeavesCoreIntact()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            SpecialRuleDefinition armyRule = NamedRule("Custom Army Rule");
            ArmyListFile army = new ArmyListFile { RuleDefinitions = new() { armyRule } };

            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);

            Assert.That(resolver.TryResolve("Custom Army Rule", out ResolvedRule added), Is.True);
            Assert.That(added.Definition, Is.SameAs(armyRule));
            Assert.That(resolver.TryResolve("Furious", out _), Is.True, "core rules remain registered.");
        }

        [Test]
        public void NoEmbeddedDefinitions_LeavesResolverUnchanged()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            ArmyListFile army = new ArmyListFile(); // RuleDefinitions defaults to empty

            ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);

            Assert.That(resolver.TryResolve("Stealth", out ResolvedRule core), Is.True);
            Assert.That(core.Definition, Is.SameAs(CoreRuleCatalog.Stealth));
        }
    }
}
