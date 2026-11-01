using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #267 — a rule that repositions or re-activates the WHOLE bearer unit must gate on rule ownership
    // (MostModelsHaveThisRule since #428 / OPR 3.6; EveryModelHasThisRule for the two rules whose 3.6 text
    // keeps "all models"), or a single joined hero carrying it hands the ability to a unit that doesn't
    // have it. Teleport shipped ungated (a hero with Teleport joined to a squad without it teleported the
    // whole squad); Vanguard, Fanatic and Martial Prowess had the same shape, and RuleValidator's #183 gate
    // check never saw any of them because it only inspects PASSIVE Subject-seat defensive entries.
    //
    // The lint here is the standing guard: it runs over the whole core catalog, so a future ungated one
    // fails the suite rather than shipping.
    [TestFixture]
    public class UnitWideAbilityGateTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();
        private static readonly HookEntry[] NoPassive = System.Array.Empty<HookEntry>();

        private static readonly TargetSelector Self =
            new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false);

        [Test]
        public void EveryCoreCatalogRule_ThatMovesOrReactivatesItsWholeUnit_GatesOnRuleOwnership()
        {
            var validator = new RuleValidator();

            var offenders = new List<string>();
            foreach (SpecialRuleDefinition rule in CoreRuleCatalog.All)
            {
                foreach (RuleViolation violation in validator.ValidateAuthoring(rule))
                {
                    if (violation.Describe().Contains("repositions or re-activates"))
                    {
                        offenders.Add($"{rule.Name} @ {violation.Hook} ({violation.Member})");
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "these rules move or re-activate the whole unit without requiring most models to have them.");
        }

        // The four the audit found. Named individually so a regression says which rule broke, not just
        // "something in the catalog". #428: OPR 3.6 moved three of them to "most models"; Martial Prowess
        // keeps "if all models in this unit have this rule", so it alone stays on the strict gate.
        [TestCase("Teleport", false)]
        [TestCase("Vanguard", false)]
        [TestCase("Fanatic", false)]
        [TestCase("Martial Prowess", true)]
        public void TheAuditedRules_CarryTheGate(string ruleName, bool keepsAllModels)
        {
            SpecialRuleDefinition rule = CoreRuleCatalog.All.Single(r => r.Name == ruleName);

            Assert.That(rule.Activated, Is.Not.Empty, $"{ruleName} is an activated ability.");
            foreach (ActivatedAbility ability in rule.Activated)
            {
                if (keepsAllModels)
                {
                    Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.EveryModelHasThisRule>(),
                        $"{ruleName}'s 3.6 text still says 'if ALL models in this unit have this rule'.");
                }
                else
                {
                    Assert.That(GatesOnMostModels(ability.AvailableWhen), Is.True,
                        $"{ruleName}'s ability must be offered only when most models in the unit have it.");
                }
            }
        }

        [Test]
        public void AnUngatedTeleport_IsRejected()
        {
            var ungated = new SpecialRuleDefinition("Test Teleport", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                        Self, new Effect.Teleport(), new Condition.Always()),
                });

            IReadOnlyList<RuleViolation> violations = new RuleValidator().ValidateAuthoring(ungated);

            Assert.That(violations, Is.Not.Empty);
            Assert.That(violations[0].Describe(), Does.Contain("MostModelsHaveThisRule"));
        }

        [Test]
        public void AGatedTeleport_IsAccepted()
        {
            var gated = new SpecialRuleDefinition("Test Teleport", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                        Self, new Effect.Teleport(), new Condition.MostModelsHaveThisRule()),
                });

            Assert.That(new RuleValidator().ValidateAuthoring(gated), Is.Empty);
        }

        // #428: the strict gate (Inquisitorial Agent, Martial Prowess) is a stronger requirement than most
        // models, so it satisfies the whole-unit check too.
        [Test]
        public void AStrictlyGatedReactivation_IsAccepted()
        {
            var gated = new SpecialRuleDefinition("Test Prowess", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnNextActivatorRequested, new Cost.OncePerGame(),
                        Self, new Effect.Reactivate(), new Condition.EveryModelHasThisRule()),
                });

            Assert.That(new RuleValidator().ValidateAuthoring(gated), Is.Empty);
        }

        // #428: the old "allModelsHaveThisRule" kind still loads (old army files embed it) and still counts as
        // the gate there - it IS the most-models condition - but newly authored data must name what it means.
        [Test]
        public void TheLegacyAllModelsKind_LoadsAsTheGate_ButAuthoringRefusesIt()
        {
            var legacy = new SpecialRuleDefinition("Test Teleport", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                        Self, new Effect.Teleport(), new Condition.LegacyAllModelsHaveThisRule()),
                });

            Assert.That(new RuleValidator().Validate(legacy), Is.Empty,
                "the load gate accepts the legacy kind, or every pre-3.6 army file would refuse to open.");

            IReadOnlyList<RuleViolation> authoring = new RuleValidator().ValidateAuthoring(legacy);
            Assert.That(authoring, Has.Count.EqualTo(1),
                "only the legacy-kind refusal - the legacy condition still satisfies the whole-unit gate.");
            Assert.That(authoring[0].Describe(), Does.Contain("allModelsHaveThisRule")
                .And.Contain("mostModelsHaveThisRule").And.Contain("everyModelHasThisRule"));
        }

        [Test]
        public void AnUngatedRepositionAtActivation_IsRejected_OnThePassivePath()
        {
            // The shape the shipped supplement used for Wolfborn / Rapid Blink / Bounding: a PASSIVE entry,
            // not an activated ability, so the check has to cover both paths.
            var ungated = new SpecialRuleDefinition("Test Blink",
                new[]
                {
                    new HookEntry(EHookID.Activation_OnActivationStart, new Condition.Always(),
                        new Effect.RepositionAtActivation(new DiceExpression.D3()),
                        ELifetime.ThisActivation, ERuleSeat.Actor),
                },
                NoAbilities);

            Assert.That(new RuleValidator().ValidateAuthoring(ungated), Is.Not.Empty);
        }

        [Test]
        public void ASupportAbilityThatMovesSomeoneELSE_IsNotGated()
        {
            // Re-Position Artillery: "pick one friendly unit within 6in with Artillery, which may move 9in".
            // The bearer isn't the one moving, so the all-models reading doesn't apply - gating it would
            // wrongly stop a hero from ordering a move.
            var support = new SpecialRuleDefinition("Test Artillery Order", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, new Cost.OncePerActivation(),
                        new TargetSelector(6f, 1, 1, ETargetAffinity.Friend, false),
                        new Effect.TriggeredMove(MaxInches: 9f, IsOptional: true),
                        new Condition.Always()),
                });

            Assert.That(new RuleValidator().ValidateAuthoring(support), Is.Empty);
        }

        [Test]
        public void APostShootHarassingMove_IsNotGated()
        {
            // The Harassing family ("after this unit shoots it may move up to 3in") shares TriggeredMove but
            // is a different class of rule and was deliberately left out of scope - only the pre-game
            // Scout/Vanguard reposition at the deploy hook is covered.
            var harassing = new SpecialRuleDefinition("Test Harassing",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnPostShoot, new Condition.Always(),
                        new Effect.TriggeredMove(MaxInches: 3f, IsOptional: true),
                        ELifetime.ThisActivation, ERuleSeat.Actor),
                },
                NoAbilities);

            Assert.That(new RuleValidator().ValidateAuthoring(harassing), Is.Empty);
        }

        [Test]
        public void TheLoadGate_StaysTolerant_SoOldArmyFilesStillOpen()
        {
            // Supplement definitions travel inside saved .fdgarmy files, and Validate() hard-fails the whole
            // army load. An army exported before this change embeds an ungated Wolfborn; it must still open.
            var ungated = new SpecialRuleDefinition("Test Blink",
                new[]
                {
                    new HookEntry(EHookID.Activation_OnActivationStart, new Condition.Always(),
                        new Effect.RepositionAtActivation(new DiceExpression.D3()),
                        ELifetime.ThisActivation, ERuleSeat.Actor),
                },
                NoAbilities);

            Assert.That(new RuleValidator().Validate(ungated), Is.Empty,
                "the load gate must not reject data the authoring gate rejects, or old saves break.");
            Assert.That(new RuleValidator().ValidateAuthoring(ungated), Is.Not.Empty,
                "...but authoring new data like that must still fail.");
        }

        private static bool GatesOnMostModels(Condition condition) => condition switch
        {
            Condition.MostModelsHaveThisRule => true,
            Condition.And and => GatesOnMostModels(and.Left) || GatesOnMostModels(and.Right),
            _ => false,
        };
    }
}
