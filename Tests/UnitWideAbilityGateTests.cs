using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #265 — a rule that repositions or re-activates the WHOLE bearer unit must gate on
    // AllModelsHaveThisRule, or a single joined hero carrying it hands the ability to a unit that doesn't
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
        public void EveryCoreCatalogRule_ThatMovesOrReactivatesItsWholeUnit_GatesOnAllModels()
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
                "these rules move or re-activate the whole unit without requiring every model to have them.");
        }

        // The four the audit found. Named individually so a regression says which rule broke, not just
        // "something in the catalog".
        [TestCase("Teleport")]
        [TestCase("Vanguard")]
        [TestCase("Fanatic")]
        [TestCase("Martial Prowess")]
        public void TheAuditedRules_CarryTheGate(string ruleName)
        {
            SpecialRuleDefinition rule = CoreRuleCatalog.All.Single(r => r.Name == ruleName);

            Assert.That(rule.Activated, Is.Not.Empty, $"{ruleName} is an activated ability.");
            foreach (ActivatedAbility ability in rule.Activated)
            {
                Assert.That(MentionsAllModels(ability.AvailableWhen), Is.True,
                    $"{ruleName}'s ability must be offered only when every model in the unit has it.");
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
            Assert.That(violations[0].Describe(), Does.Contain("AllModelsHaveThisRule"));
        }

        [Test]
        public void AGatedTeleport_IsAccepted()
        {
            var gated = new SpecialRuleDefinition("Test Teleport", NoPassive,
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnActionChoice, new Cost.OncePerActivation(),
                        Self, new Effect.Teleport(), new Condition.AllModelsHaveThisRule()),
                });

            Assert.That(new RuleValidator().ValidateAuthoring(gated), Is.Empty);
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
                    new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
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

        private static bool MentionsAllModels(Condition condition) => condition switch
        {
            Condition.AllModelsHaveThisRule => true,
            Condition.And and => MentionsAllModels(and.Left) || MentionsAllModels(and.Right),
            _ => false,
        };
    }
}
