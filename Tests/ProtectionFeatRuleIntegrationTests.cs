using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Tests.RulesHarness;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #197 misc - Protection Feat ("once per game, when this unit takes wounds ... you may roll one die per
    // wound, ignoring each on a 5+"). Modelled as a proactive OPTIONAL brace: a single once-per-game
    // activation-start ability (Yes/No via the same stage path as Speed Feat) grants a roll-per-wound 5+
    // wound-ignore that lasts UntilNextActivation - so it covers the incoming fire on the opponent's turn.
    // The reactive "react when hit" timing is reinterpreted as "brace in advance"; the effect (once-per-game,
    // per-wound 5+ ignore, all-models, optional) is faithful. Reuses Regeneration's IgnoreWoundOnRoll.
    [TestFixture]
    public class ProtectionFeatRuleIntegrationTests
    {
        private const string Guard = "Protection Feat Guard";

        private static SpecialRuleDefinition ProtectionFeat() => new("Protection Feat",
            Array.Empty<HookEntry>(),
            new[]
            {
                new ActivatedAbility(EHookID.Activation_OnActivationStart,
                    new Cost.OncePerGame(),
                    new TargetSelector(RangeInches: 0f, MinCount: 1, MaxCount: 1, ETargetAffinity.Self,
                        RequireLineOfSight: false),
                    new Effect.AddRule(Guard, ELifetime.UntilNextActivation),
                    new Condition.AllModelsHaveThisRule(),
                    Label: "Protection Feat"),
            });

        private static SpecialRuleDefinition ProtectionFeatGuard() => new(Guard,
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.AllModelsHaveThisRule(),
                    new Effect.IgnoreWoundOnRoll(MinRoll: 5), ELifetime.ThisAttack, ERuleSeat.Subject),
            },
            Array.Empty<ActivatedAbility>());

        private static TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            harness.Register(ProtectionFeat());
            harness.Register(ProtectionFeatGuard());
            return harness;
        }

        private static WoundIgnoreSink IgnoreAgainst(TestRuleHarness harness, IUnit attacker, IUnit defender)
        {
            var sink = new WoundIgnoreSink();
            sink.ApplyFrom(harness.Evaluate(defender, ERuleSeat.Subject,
                new SaveRollCompleteContext(attacker, defender, Faces(4))));
            return sink;
        }

        [Test]
        public void ProtectionFeat_OffersASingleOptionalAbility_AtActivationStart()
        {
            TestRuleHarness harness = Harness();
            IUnit unit = harness.BuildUnit("P1", 1, "Protection Feat");

            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new ActivationStartContext(unit));
            Assert.That(offers, Has.Count.EqualTo(1), "single ability -> the stage offers Yes/No.");
            Assert.That(offers.Single().RuleName, Is.EqualTo("Protection Feat"));
        }

        [Test]
        public void Bracing_GrantsARollPerWoundFivePlusIgnore()
        {
            TestRuleHarness harness = Harness();
            IUnit defender = harness.BuildUnit("P1", 1, "Protection Feat");
            IUnit attacker = harness.BuildUnit("P2", 1);

            Assert.That(IgnoreAgainst(harness, attacker, defender).HasIgnore, Is.False,
                "no protection before the feat is used.");

            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(defender)).Single();
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, defender));

            WoundIgnoreSink ignore = IgnoreAgainst(harness, attacker, defender);
            Assert.That(ignore.HasIgnore, Is.True, "after bracing, incoming wounds may be ignored...");
            Assert.That(ignore.Threshold, Is.EqualTo(5), "...on a 5+.");
        }

        [Test]
        public void TheGrantLastsUntilTheNextActivation_ThenExpires()
        {
            TestRuleHarness harness = Harness();
            IUnit defender = harness.BuildUnit("P1", 1, "Protection Feat");
            IUnit attacker = harness.BuildUnit("P2", 1);

            AbilityOffer offer = harness.OfferAbilities(new ActivationStartContext(defender)).Single();
            OperationApplier.ApplyTokenOperations(harness.Accept(offer, defender));
            Assert.That(IgnoreAgainst(harness, attacker, defender).HasIgnore, Is.True,
                "live through the opponent's turn (UntilNextActivation), which is when the wounds come.");

            // The unit's next activation sweeps UntilNextActivation grants.
            new TokenClearService().ClearForHook(EHookID.Activation_OnActivationStart,
                new List<ITokenContainer> { defender.Tokens });
            Assert.That(IgnoreAgainst(harness, attacker, defender).HasIgnore, Is.False,
                "spent brace does not persist into the next activation.");
        }

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }
    }
}
