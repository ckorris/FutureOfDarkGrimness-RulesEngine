using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #441 F7 (signed off B): the base rule of an Enhanced rule read off its gate, a one-activation helper folded to
    /// the rule whose activation pick grants it, and the display-only "Base rule: X." line for a text that never
    /// names its base. Shapes mirror the shipped supplements (Watchborn / Vinci Tech; Lustbound; Furious Buff).
    /// </summary>
    [TestFixture]
    public class EnhancedRuleGateTests
    {
        private static readonly IReadOnlyList<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();
        private static readonly IReadOnlyList<HookEntry> NoPassive = new List<HookEntry>();

        private static SpecialRuleDefinition Gated(string name, Condition gate, string description) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared, gate,
                        new Effect.MovementBonus(EActionType.Advance, 1f, 0f), ELifetime.ThisActivation),
                },
                NoAbilities, Description: description);

        /// <summary>The Watchborn shape: an activation pick that grants one of two helpers to its own unit.</summary>
        private static SpecialRuleDefinition Picker(string name, ETargetAffinity affinity, params string[] helpers)
        {
            List<ActivatedAbility> abilities = new List<ActivatedAbility>();
            foreach (string helper in helpers)
            {
                abilities.Add(new ActivatedAbility(EHookID.Activation_OnActivationStart, new Cost.OncePerActivation(),
                    new TargetSelector(0f, 1, 1, affinity, false),
                    new Effect.AddRule(helper, ELifetime.ThisActivation), new Condition.Always(), helper));
            }

            return new SpecialRuleDefinition(name, NoPassive, abilities, Description: $"Pick one effect from {name}.");
        }

        private const string VersatileText =
            "If this model has Versatile Attack, it always gets either AP(+1) or +1 to hit rolls from Versatile Attack.";

        private static SpecialRuleDefinition EnhancedWatchborn() => new SpecialRuleDefinition("Enhanced Watchborn",
            new List<HookEntry>
            {
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.And(new Condition.UnitHasRule("Watchborn (Piercing)"), new Condition.Not(new Condition.Always())),
                    new Effect.MovementBonus(EActionType.Advance, 1f, 0f), ELifetime.ThisActivation),
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.And(new Condition.UnitHasRule("Watchborn (Precision)"), new Condition.Not(new Condition.Always())),
                    new Effect.MovementBonus(EActionType.Advance, 1f, 0f), ELifetime.ThisActivation),
            },
            NoAbilities, Description: VersatileText);

        private static RuleResolver Resolver(params SpecialRuleDefinition[] definitions)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in definitions) resolver.RegisterOrReplace(definition);
            return resolver;
        }

        [Test]
        public void GateRuleNames_WalkAndOr_NotNot_NotTheRuleItself()
        {
            SpecialRuleDefinition rule = Gated("Enhanced Oddly Named",
                new Condition.Or(
                    new Condition.And(new Condition.UnitHasRule("Lustbound"), new Condition.WeaponHasRule("Parry")),
                    new Condition.And(new Condition.Not(new Condition.UnitHasRule("Fearless")), new Condition.UnitHasRule("Enhanced Oddly Named"))),
                "text");

            Assert.That(EnhancedRuleGate.GateRuleNames(rule), Is.EqualTo(new[] { "Lustbound", "Parry" }));
        }

        [Test]
        public void AHelper_FoldsToTheRuleWhoseSelfTargetedPickGrantsIt()
        {
            RuleResolver resolver = Resolver(Picker("Watchborn", ETargetAffinity.Self, "Watchborn (Piercing)", "Watchborn (Precision)"),
                EnhancedWatchborn());

            Assert.Multiple(() =>
            {
                Assert.That(EnhancedRuleGate.GateRuleNames(EnhancedWatchborn()),
                    Is.EqualTo(new[] { "Watchborn (Piercing)", "Watchborn (Precision)" }), "the raw gate");
                Assert.That(EnhancedRuleGate.BaseRuleNames(EnhancedWatchborn(), resolver), Is.EqualTo(new[] { "Watchborn" }),
                    "both helpers fold to the one rule the unit carries");
                Assert.That(EnhancedRuleGate.BaseRuleNames(EnhancedWatchborn(), (IRuleResolver?)null),
                    Is.EqualTo(new[] { "Watchborn (Piercing)", "Watchborn (Precision)" }), "no candidates: the gate stands");
                Assert.That(EnhancedRuleGate.GrantingRule("Watchborn (Piercing)", resolver.Definitions)?.Name, Is.EqualTo("Watchborn"));
            });
        }

        [Test]
        public void AGrantAimedAtAnotherUnit_IsNotTheBase()
        {
            // Furious Buff hands Furious to a friend (Friend-targeted, NextTrigger): "Enhanced Furious" still depends
            // on Furious itself, and a Self-less picker of the same shape does not fold either.
            RuleResolver resolver = Resolver(Picker("Guarded Buff", ETargetAffinity.Friend, "Guarded"));
            SpecialRuleDefinition enhancedFurious = Gated("Enhanced Furious", new Condition.UnitHasRule("Furious"), "If this model has Furious, more.");
            SpecialRuleDefinition enhancedGuarded = Gated("Enhanced Guarded", new Condition.UnitHasRule("Guarded"), "If this model has Guarded, more.");

            Assert.Multiple(() =>
            {
                Assert.That(resolver.TryResolve("Furious Buff", out _), Is.True, "the catalog's Buff is the real shape");
                Assert.That(EnhancedRuleGate.BaseRuleNames(enhancedFurious, resolver), Is.EqualTo(new[] { "Furious" }));
                Assert.That(EnhancedRuleGate.BaseRuleNames(enhancedGuarded, resolver), Is.EqualTo(new[] { "Guarded" }));
                Assert.That(EnhancedRuleGate.GrantingRule("Guarded", resolver.Definitions), Is.Null);
            });
        }

        [Test]
        public void BaseRuleLine_OnlyWhenTheTextNamesNeitherTheBaseNorItsGate()
        {
            RuleResolver resolver = Resolver(Picker("Watchborn", ETargetAffinity.Self, "Watchborn (Piercing)", "Watchborn (Precision)"));
            SpecialRuleDefinition namesIt = Gated("Enhanced Lustbound", new Condition.UnitHasRule("Lustbound"),
                "If this model has Lustbound, it moves further.");
            SpecialRuleDefinition namesItLowercase = Gated("Enhanced Lustbound", new Condition.UnitHasRule("Lustbound"),
                "A model with lustbound moves further.");
            SpecialRuleDefinition namesTheHelper = Gated("Enhanced Watchborn", new Condition.UnitHasRule("Watchborn (Piercing)"),
                "If this model has Watchborn (Piercing), always.");
            SpecialRuleDefinition noGate = new SpecialRuleDefinition("Enhanced Lustbound Aura", NoPassive, NoAbilities,
                Description: "This model and its unit get Enhanced Lustbound.");
            SpecialRuleDefinition notEnhanced = Gated("Oddly Gated", new Condition.UnitHasRule("Lustbound"), "Says nothing of its gate.");
            SpecialRuleDefinition undocumented = Gated("Enhanced Silent", new Condition.UnitHasRule("Lustbound"), "");

            Assert.Multiple(() =>
            {
                Assert.That(EnhancedRuleGate.BaseRuleLine(EnhancedWatchborn(), resolver), Is.EqualTo("Base rule: Watchborn."));
                Assert.That(EnhancedRuleGate.HoverDescription(EnhancedWatchborn(), resolver),
                    Is.EqualTo(VersatileText + "\nBase rule: Watchborn."), "the OPR text first, untouched, then the line");
                Assert.That(EnhancedRuleGate.BaseRuleLine(namesIt, resolver), Is.Null, "the text names its base");
                Assert.That(EnhancedRuleGate.BaseRuleLine(namesItLowercase, resolver), Is.Null, "case-insensitive");
                Assert.That(EnhancedRuleGate.BaseRuleLine(namesTheHelper, resolver), Is.Null, "naming the gate itself counts");
                Assert.That(EnhancedRuleGate.BaseRuleLine(noGate, resolver), Is.Null, "an Aura depends on nothing");
                Assert.That(EnhancedRuleGate.BaseRuleLine(notEnhanced, resolver), Is.Null, "only the Enhanced family");
                Assert.That(EnhancedRuleGate.BaseRuleLine(undocumented, resolver), Is.Null, "no text, no line");
                Assert.That(EnhancedRuleGate.HoverDescription(namesIt, resolver), Is.EqualTo(namesIt.Description), "unchanged");
            });
        }

        [Test]
        public void BaseRuleLine_SeveralUnnamedBases_AreJoined_WithoutCandidatesTheGateNamesStand()
        {
            SpecialRuleDefinition two = Gated("Enhanced Two",
                new Condition.Or(new Condition.UnitHasRule("Lustbound"), new Condition.WeaponHasRule("Parry")), "Says nothing.");

            Assert.Multiple(() =>
            {
                Assert.That(EnhancedRuleGate.BaseRuleLine(two, (IRuleResolver?)null), Is.EqualTo("Base rules: Lustbound and Parry."));
                Assert.That(EnhancedRuleGate.BaseRuleLine(EnhancedWatchborn(), (IRuleResolver?)null),
                    Is.EqualTo("Base rules: Watchborn (Piercing) and Watchborn (Precision)."));
            });
        }
    }
}
