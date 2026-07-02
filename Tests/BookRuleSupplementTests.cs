using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #153 rule supplement: the curated supplement file is the durable home for authored faction-rule
    // definitions (books are regenerated importer artifacts). Apply() must embed exactly the referenced
    // subset — plus definitions those definitions grant — replace-by-name idempotently, and hard-fail
    // on definitions that would fail the #059 load gate.
    [TestFixture]
    public class BookRuleSupplementTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();

        private static SpecialRuleDefinition MovementRule(string name) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        new Condition.ActionTypeIs(EActionType.Advance),
                        new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                        ELifetime.ThisActivation),
                },
                NoAbilities);

        private static SpecialRuleDefinition AuraRule(string name, string grants) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.Always(),
                        new Effect.Aura(grants),
                        ELifetime.UntilEndOfGame),
                },
                NoAbilities);

        private static BookFile BookReferencing(params string[] ruleNames)
        {
            return new BookFile
            {
                Name = "Test Book",
                Units = new List<RosterUnit>
                {
                    new RosterUnit
                    {
                        Id = "u1",
                        Name = "Testers",
                        Rules = ruleNames.Select(n => (SpecialRuleEntry)new SpecialRuleEntry_Core(n)).ToList(),
                    },
                },
            };
        }

        [Test]
        public void Apply_EmbedsOnlyReferencedDefinitions()
        {
            BookFile book = BookReferencing("Highborn");
            var supplement = new List<SpecialRuleDefinition>
            {
                MovementRule("Highborn"),
                MovementRule("Hive Bond Sprint"), // unreferenced — must not embed
            };

            IReadOnlyList<string> embedded = BookRuleSupplement.Apply(book, supplement);

            Assert.That(embedded, Is.EqualTo(new[] { "Highborn" }));
            Assert.That(book.RuleDefinitions.Select(d => d.Name), Is.EqualTo(new[] { "Highborn" }));
        }

        [Test]
        public void Apply_PullsInGrantedDefinitionsTransitively()
        {
            // Book references only the aura; the aura grants the boost, which grants nothing further.
            BookFile book = BookReferencing("Boost Aura");
            var supplement = new List<SpecialRuleDefinition>
            {
                AuraRule("Boost Aura", "Boost"),
                MovementRule("Boost"),
            };

            IReadOnlyList<string> embedded = BookRuleSupplement.Apply(book, supplement);

            Assert.That(embedded, Is.EquivalentTo(new[] { "Boost Aura", "Boost" }));
        }

        [Test]
        public void Apply_SeedsFromSpellGrantedNames()
        {
            // No unit references the rule; only a spell grants it ("gets X once" shape).
            BookFile book = BookReferencing();
            book.Spells.Add(new SpellDefinition("Blessing", 2,
                new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                new Effect.AddRule("Boost", ELifetime.NextTrigger)));
            var supplement = new List<SpecialRuleDefinition> { MovementRule("Boost") };

            IReadOnlyList<string> embedded = BookRuleSupplement.Apply(book, supplement);

            Assert.That(embedded, Is.EqualTo(new[] { "Boost" }));
        }

        [Test]
        public void Apply_SeedsFromAliasedRuleNames()
        {
            // "Psy-Marker (Piercing Spotter)" style: the alias target counts as referenced.
            BookFile book = new BookFile
            {
                Units = new List<RosterUnit>
                {
                    new RosterUnit
                    {
                        Rules = new List<SpecialRuleEntry>
                        {
                            new SpecialRuleEntry_Alias("Psy-Marker", new SpecialRuleEntry_Core("Spotter")),
                        },
                    },
                },
            };
            var supplement = new List<SpecialRuleDefinition> { MovementRule("Spotter") };

            IReadOnlyList<string> embedded = BookRuleSupplement.Apply(book, supplement);

            Assert.That(embedded, Is.EqualTo(new[] { "Spotter" }));
        }

        [Test]
        public void Apply_IsIdempotent_ReplacingByName()
        {
            BookFile book = BookReferencing("Highborn");
            var supplement = new List<SpecialRuleDefinition> { MovementRule("Highborn") };

            BookRuleSupplement.Apply(book, supplement);
            BookRuleSupplement.Apply(book, supplement);

            Assert.That(book.RuleDefinitions.Count(d => d.Name == "Highborn"), Is.EqualTo(1));
        }

        [Test]
        public void Apply_ThrowsWhenGrantedNameResolvesNowhere()
        {
            BookFile book = BookReferencing("Bad Aura");
            var supplement = new List<SpecialRuleDefinition>
            {
                AuraRule("Bad Aura", "No Such Rule"),
            };

            var ex = Assert.Throws<InvalidOperationException>(() => BookRuleSupplement.Apply(book, supplement));
            Assert.That(ex!.Message, Does.Contain("No Such Rule"));
            Assert.That(book.RuleDefinitions, Is.Empty, "a failed apply must not partially embed");
        }

        [Test]
        public void Apply_ThrowsOnCapabilityViolation()
        {
            // A distance condition on a lifecycle hook (no IHasDistance) — the same shape the #059
            // load gate rejects; the supplement must reject it earlier, at apply time.
            BookFile book = BookReferencing("Misplaced");
            var supplement = new List<SpecialRuleDefinition>
            {
                new SpecialRuleDefinition("Misplaced",
                    new List<HookEntry>
                    {
                        new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                            new Condition.DistanceGreaterThan(9f),
                            new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                            ELifetime.ThisAttack),
                    },
                    NoAbilities),
            };

            Assert.Throws<InvalidOperationException>(() => BookRuleSupplement.Apply(book, supplement));
        }

        [Test]
        public void Apply_GrantedNameMayResolveInCoreCatalog()
        {
            // "Stealth Buff" grants core "Stealth" — no supplement definition needed for the grantee.
            BookFile book = BookReferencing("Stealth Buff");
            var supplement = new List<SpecialRuleDefinition>
            {
                new SpecialRuleDefinition("Stealth Buff",
                    new List<HookEntry>(),
                    new List<ActivatedAbility>
                    {
                        new ActivatedAbility(EHookID.Activation_OnPreAttack, new Cost.OncePerActivation(),
                            new TargetSelector(12f, 1, 1, ETargetAffinity.Friend, false),
                            new Effect.AddRule("Stealth", ELifetime.NextTrigger),
                            new Condition.Always()),
                    }),
            };

            Assert.DoesNotThrow(() => BookRuleSupplement.Apply(book, supplement));
        }

        [Test]
        public void ValidateAll_ReportsDuplicateNames()
        {
            var supplement = new List<SpecialRuleDefinition>
            {
                MovementRule("Twin"),
                MovementRule("twin"), // case-insensitive duplicate
            };

            IReadOnlyList<string> problems = BookRuleSupplement.ValidateAll(supplement);

            Assert.That(problems, Has.Some.Contains("Duplicate"));
        }

        [Test]
        public void LoadDefinitions_RoundTripsThroughRuleJson()
        {
            var original = new List<SpecialRuleDefinition> { AuraRule("Boost Aura", "Boost") };
            string json = System.Text.Json.JsonSerializer.Serialize(original,
                FDG.Rules.Serialization.RuleJson.Options);

            List<SpecialRuleDefinition> loaded = BookRuleSupplement.LoadDefinitions(json);

            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0].Name, Is.EqualTo("Boost Aura"));
            Assert.That(((Effect.Aura)loaded[0].Passive[0].Effect).RuleName, Is.EqualTo("Boost"));
        }
    }
}
