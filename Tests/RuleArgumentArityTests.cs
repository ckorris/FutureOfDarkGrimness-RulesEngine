using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #059 workstream 4: a definition whose effect reads Arg(n) must only attach when the referencing
    // army entry supplies at least n+1 arguments. ResolveForScope catches the shortfall at attach
    // (skip-with-warning, like its unimplemented/misscoped branches) instead of letting it become a
    // raw IndexOutOfRangeException in ValueSource.Arg.Resolve mid-dispatch.
    [TestFixture]
    public class RuleArgumentArityTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();

        // A definition whose only effect multiplies wounds by argument 0 (the Deadly(X) shape).
        private static SpecialRuleDefinition ArgReadingRule(string name) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.Always(),
                        new Effect.MultiplyWounds(new ValueSource.Arg(0)),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

        // The resolver matches rule names case-insensitively, so a corpus reference written "Bane when
        // Shooting" resolves to the catalog rule registered "Bane when shooting" (and any casing in
        // between). Guards the fix for the case-sensitive-resolution gap.
        [Test]
        public void Resolver_IsCaseInsensitive_CorpusCasingResolvesToCatalogRule()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();

            Assert.That(resolver.TryResolve("Bane when Shooting", out ResolvedRule corpusCased), Is.True,
                "corpus casing 'Bane when Shooting' must resolve");
            Assert.That(resolver.TryResolve("bane when shooting", out ResolvedRule allLower), Is.True);

            Assert.That(corpusCased.Definition, Is.SameAs(CoreRuleCatalog.BaneWhenShooting),
                "resolves to the registered definition regardless of casing");
            Assert.That(allLower.Definition, Is.SameAs(corpusCased.Definition));
        }

        [Test]
        public void MaxReferencedArgIndex_NoArgs_IsNegativeOne()
        {
            SpecialRuleDefinition plain = new SpecialRuleDefinition("Plain",
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.UnmodifiedRollEquals(6),
                        new Effect.AddExtraHit(OnRollValue: 6),
                        ELifetime.ThisAttack),
                },
                NoAbilities);

            Assert.That(RuleArgumentArity.MaxReferencedArgIndex(plain), Is.EqualTo(-1));
        }

        [Test]
        public void MaxReferencedArgIndex_ReadsArgZero_IsZero()
        {
            Assert.That(RuleArgumentArity.MaxReferencedArgIndex(ArgReadingRule("Deadly")), Is.EqualTo(0));
        }

        [Test]
        public void ResolveForScope_WithEnoughArguments_Attaches()
        {
            RuleResolver resolver = new RuleResolver();
            resolver.Register(ArgReadingRule("Deadly"));

            // CoreNumeric supplies one argument (index 0) — enough for Arg(0).
            ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                resolver, new SpecialRuleEntry_CoreNumeric("Deadly", 3), ERuleScope.Unit, "test unit");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Arguments, Has.Count.EqualTo(1));
        }

        [Test]
        public void ResolveForScope_MissingArgument_SkipsInsteadOfAttaching()
        {
            RuleResolver resolver = new RuleResolver();
            resolver.Register(ArgReadingRule("Deadly"));

            // A plain Core reference supplies zero arguments — Arg(0) can't bind.
            ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                resolver, new SpecialRuleEntry_Core("Deadly"), ERuleScope.Unit, "test unit");

            Assert.That(resolved, Is.Null,
                "a rule reading Arg(0) must not attach when the reference supplies no argument.");
        }
    }
}
