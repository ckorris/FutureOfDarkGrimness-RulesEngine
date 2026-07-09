using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 P5a — the dormant Activation_OnActivationStart hook, and the "pick one effect until the end of the
    // activation" shape the corpus hangs there (Versatile Attack, Watchborn, Versatile Reach).
    //
    // The rule carries one ActivatedAbility per effect, all at that hook, each labelled. What must hold:
    //   * both effects are offered, distinguishable (AbilityOffer.RuleName is the same for both);
    //   * choosing one grants its helper rule for THIS activation only;
    //   * the once-per-activation cost is keyed on the RULE, so taking one effect spends the other;
    //   * the granted rule is read back at the hooks it fires on, and expires at activation end.
    [TestFixture]
    public class ActivationStartChoiceTests
    {
        private const string PiercingHelper = "Versatile Piercing";
        private const string PrecisionHelper = "Versatile Precision";

        /// <summary>A "pick one effect" rule: two labelled abilities at the activation-start hook, each
        /// granting a different helper rule for the activation. Mirrors the real Versatile Attack.</summary>
        private static SpecialRuleDefinition VersatileAttack() => new("Versatile Attack",
            Array.Empty<HookEntry>(),
            new[]
            {
                SelfGrant("AP(+1)", PiercingHelper),
                SelfGrant("+1 to hit", PrecisionHelper),
            });

        private static ActivatedAbility SelfGrant(string label, string grantedRule) =>
            new(EHookID.Activation_OnActivationStart,
                new Cost.OncePerActivation(),
                new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                    RequireLineOfSight: false),
                new Effect.AddRule(grantedRule, ELifetime.ThisActivation),
                new Condition.Always(),
                Label: label);

        private static SpecialRuleDefinition Piercing() => new(PiercingHelper,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private static SpecialRuleDefinition Precision() => new(PrecisionHelper,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollModifier, new Condition.Always(),
                    new Effect.RollModifier(ERollKind.Hit, +1), ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private static TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            harness.Register(VersatileAttack());
            harness.Register(Piercing());
            harness.Register(Precision());
            return harness;
        }

        [Test]
        public void BothEffects_AreOfferedAtActivationStart_AndAreTellableApartOnlyByLabel()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Versatile Attack");

            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new ActivationStartContext(unit));

            Assert.That(offers, Has.Count.EqualTo(2));
            Assert.That(offers.Select(o => o.RuleName).Distinct().Single(), Is.EqualTo("Versatile Attack"),
                "Both offers come from one rule - which is exactly why the choice needs the ability's label.");
            Assert.That(offers.Select(o => o.Ability.Label),
                Is.EquivalentTo(new[] { "AP(+1)", "+1 to hit" }));
        }

        [Test]
        public void ChoosingAnEffect_GrantsItsHelperRule_ForThisActivationOnly()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Versatile Attack");

            AbilityOffer piercing = harness.OfferAbilities(new ActivationStartContext(unit))
                .Single(o => o.Ability.Label == "AP(+1)");

            IReadOnlyList<RuleOperation> ops = harness.Accept(piercing, unit);
            OperationApplier.ApplyTokenOperations(ops);

            List<TokenPayload.RuleGrant> grants = unit.Tokens.GetAllTokens(TokenType.RuleGrant)
                .Select(t => t.Payload).OfType<TokenPayload.RuleGrant>().ToList();

            Assert.That(grants.Select(g => g.RuleName), Does.Contain(PiercingHelper));
            Assert.That(grants.Select(g => g.RuleName), Does.Not.Contain(PrecisionHelper),
                "Picking one effect must not grant the other.");
            Assert.That(grants.Single(g => g.RuleName == PiercingHelper).Lifetime,
                Is.EqualTo(ELifetime.ThisActivation));
        }

        [Test]
        public void TakingOneEffect_SpendsTheRulesOncePerActivationGate_ForItsSibling()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Versatile Attack");

            AbilityOffer first = harness.OfferAbilities(new ActivationStartContext(unit)).First();
            OperationApplier.ApplyTokenOperations(harness.Accept(first, unit));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(unit)), Is.Empty,
                "The cost is keyed on the rule name, so choosing one effect closes the whole rule for the " +
                "activation - which is what 'pick one effect' means.");
        }

        [Test]
        public void TheGrantedEffect_ActuallyFires_AtTheHookItTargets()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Versatile Attack");
            IUnit target = harness.BuildUnit("P2", 1);

            AbilityOffer precision = harness.OfferAbilities(new ActivationStartContext(unit))
                .Single(o => o.Ability.Label == "+1 to hit");
            OperationApplier.ApplyTokenOperations(harness.Accept(precision, unit));

            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor,
                new HitRollModifierContext(unit, target, DistanceInches: 12f)));

            Assert.That(sink.Net(ERollKind.Hit), Is.EqualTo(1),
                "The granted helper rule must be read back and fire - a grant nothing consumes is the " +
                "Breath Attack failure mode.");
        }

        [Test]
        public void TheOtherEffect_DoesNotFire_WhenItWasNotChosen()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Versatile Attack");
            IUnit target = harness.BuildUnit("P2", 1);

            AbilityOffer precision = harness.OfferAbilities(new ActivationStartContext(unit))
                .Single(o => o.Ability.Label == "+1 to hit");
            OperationApplier.ApplyTokenOperations(harness.Accept(precision, unit));

            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluate(unit, ERuleSeat.Actor,
                new HitRollCompleteContext(unit, target, TestDice.Faces(4), DistanceInches: 12f)));

            Assert.That(sink.Net(ERollKind.Save), Is.EqualTo(0),
                "Choosing '+1 to hit' must not also confer the AP(+1) effect.");
        }

        [Test]
        public void ARuleOfferingOneAbility_NeedsNoLabel()
        {
            // Every pre-existing rule has exactly one ability and no label; the stage applies it without a
            // prompt. Pinned so adding Label can't have changed how single-ability rules are offered.
            var harness = new TestRuleHarness();
            harness.Register(CoreRuleCatalog.Mend);
            IUnit unit = harness.BuildUnit("P1", 1, "Mend");

            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new PreAttackContext(unit, EActionType.Hold));

            Assert.That(offers, Has.Count.EqualTo(1));
            Assert.That(offers[0].Ability.Label, Is.Empty);
        }
    }
}
