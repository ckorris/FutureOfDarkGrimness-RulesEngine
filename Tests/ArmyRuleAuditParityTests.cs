using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #168: the army-builder screen shows "N rules on this list are not implemented" from
    // ArmyRuleAudit — a store-free walk of the list's rule references. Its whole value rests on
    // reporting exactly what the launch path drops, so the central test here runs BOTH on the same
    // deliberately messy army and asserts the drop sequences match. If an attachment site gains a
    // new drop path (or stops dropping something), this fixture is what forces the audit to follow.
    [TestFixture]
    public class ArmyRuleAuditParityTests
    {
        private static readonly List<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();

        // #344: the messy army's 'Grudge' reference exercises ERuleDropReason.OutdatedList, which only
        // exists when a rulebook is installed that knows the name. MessyArmy carries no Faction, so
        // nothing is backfilled and the reference drops - classified as "your list predates this rule"
        // rather than "not implemented".
        private sealed class RulebookKnowingGrudge : ICurrentRulebook
        {
            public IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string faction) =>
                Array.Empty<SpecialRuleDefinition>();

            public bool Defines(string ruleName) =>
                string.Equals(ruleName, "Grudge", StringComparison.OrdinalIgnoreCase);
        }

        [SetUp]
        public void InstallRulebook() => CurrentRulebook.Installed = new RulebookKnowingGrudge();

        [TearDown]
        public void ClearRulebook() => CurrentRulebook.Installed = null;

        // The Deadly(X) shape: an effect reading Arg(0), so a bare Core reference (no argument)
        // trips the MissingArgument branch. Registered as an embedded definition, unit-scoped.
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

        /// <summary>One reference for every drop reason, plus healthy references that must NOT report:
        /// implemented unit/weapon rules, and unit-level wargear that legitimately re-homes (#197).</summary>
        private static ArmyListFile MessyArmy() => new ArmyListFile
        {
            Name = "Messy",
            RuleDefinitions = new List<SpecialRuleDefinition> { ArgReadingRule("Argful") },
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Berserkers", ModelCount = 2, Quality = 4, Defense = 4,
                    SpecialRules =
                    {
                        new SpecialRuleEntry_Core("Wolfborn"),       // Unimplemented (unit level)
                        new SpecialRuleEntry_Core("Grudge"),         // OutdatedList: the rulebook has it, this list doesn't (#344)
                        new SpecialRuleEntry_Core("Argful"),         // MissingArgument (reads Arg(0))
                        new SpecialRuleEntry_Core("Bane in melee"),  // weapon-scoped, re-homes: NO drop
                        new SpecialRuleEntry_Core("Stealth"),        // implemented: NO drop
                    },
                    Weapons =
                    {
                        new WeaponFileEntry
                        {
                            Name = "Chainblade", Quantity = 2, Attacks = 2,
                            SpecialRules = { new SpecialRuleEntry_Core("Chrono Field") }, // Unimplemented (weapon level)
                        },
                        new WeaponFileEntry
                        {
                            Name = "Rifle", Quantity = 2, RangeInches = 24, Attacks = 1,
                            SpecialRules = { new SpecialRuleEntry_Core("Stealth") },      // WrongScope (unit rule on a weapon)
                        },
                    },
                },
                new UnitFileEntry
                {
                    Name = "Prophet", ModelCount = 1, Quality = 3, Defense = 5,
                    SpecialRules = { new SpecialRuleEntry_Core("Bane in melee") },        // NoWeaponsToAttach
                },
            },
            Spells =
            {
                new SpellDefinition("Warp Bolt", Threshold: 2,
                    new TargetSelector(18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                    new Effect.DealHits(3, new[] { "Blast(3)", "Frobnicate" })),          // Frobnicate: Unimplemented
            },
        };

        [Test]
        public void Audit_ReportsExactlyWhatTheLaunchPathDrops()
        {
            ArmyListFile army = MessyArmy();

            // The real launch path: shared resolver (core + embedded), army built into a real store,
            // spells resolved — capturing every structured drop it emits.
            List<RuleDrop> liveDrops = new List<RuleDrop>();
            void Capture(RuleDrop drop) => liveDrops.Add(drop);

            RuleDiagnostics.OnRuleDropped += Capture;
            try
            {
                GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
                GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
            }
            finally
            {
                RuleDiagnostics.OnRuleDropped -= Capture;
            }

            ArmyRuleAuditResult audit = ArmyRuleAudit.Audit(army);

            Assert.That(liveDrops, Is.Not.Empty, "precondition: the messy army must actually drop rules at launch.");
            Assert.That(audit.EmbeddedDefinitionError, Is.Null, "the embedded Argful definition is valid.");

            // Same drops, same order (per unit: weapon entries as the UnitData ctor walks them, then
            // unit-level names; spells last) — (name, owner, reason) pins attribution, not just counts.
            Assert.That(
                audit.Drops.Select(d => (d.RuleName, d.Owner, d.Reason)),
                Is.EqualTo(liveDrops.Select(d => (d.RuleName, d.Owner, d.Reason))),
                "the builder-pane audit must report exactly what army load drops.");
        }

        [Test]
        public void Audit_CoversEveryDropReason()
        {
            // Guards the fixture itself: if the messy army stops exercising a reason (say a rule it
            // uses gets implemented), the parity test above silently loses coverage of that branch.
            ArmyRuleAuditResult audit = ArmyRuleAudit.Audit(MessyArmy());

            Assert.That(audit.Drops.Select(d => d.Reason).Distinct(),
                Is.EquivalentTo(Enum.GetValues<ERuleDropReason>()),
                "the messy army must keep exercising every ERuleDropReason.");
        }

        [Test]
        public void Audit_CleanArmy_ReportsNothing()
        {
            ArmyListFile army = new ArmyListFile
            {
                Name = "Clean",
                Units =
                {
                    new UnitFileEntry
                    {
                        Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4,
                        SpecialRules = { new SpecialRuleEntry_Core("Stealth") },
                        Weapons =
                        {
                            new WeaponFileEntry
                            {
                                Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1,
                                SpecialRules = { new SpecialRuleEntry_CoreNumeric("Deadly", 3) },
                            },
                        },
                    },
                },
            };

            ArmyRuleAuditResult audit = ArmyRuleAudit.Audit(army);

            Assert.That(audit.Drops, Is.Empty);
            Assert.That(audit.EmbeddedDefinitionError, Is.Null);
        }

        [Test]
        public void Audit_InvalidEmbeddedDefinition_ReportsTheErrorAndStillAudits()
        {
            ArmyListFile army = MessyArmy();
            // A distance condition on a lifecycle hook whose context has no IHasDistance — the shape
            // EmbeddedRuleValidationTests uses to make RegisterEmbeddedDefinitions throw.
            army.RuleDefinitions.Add(new SpecialRuleDefinition("Misplaced",
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                        new Condition.DistanceGreaterThan(9f),
                        new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                        ELifetime.ThisAttack),
                },
                NoAbilities));

            ArmyRuleAuditResult audit = ArmyRuleAudit.Audit(army);

            Assert.That(audit.EmbeddedDefinitionError, Does.Contain("Misplaced"),
                "a list that launch would reject outright must say so, not just list drops.");
            // Validation rejects ALL embedded definitions (register-nothing semantics), so the audit
            // runs core-catalog-only: 'Argful' can no longer resolve and joins the drops as
            // Unimplemented rather than MissingArgument. The walk still completes.
            Assert.That(audit.Drops.Select(d => d.RuleName), Does.Contain("Argful"));
            Assert.That(audit.Drops.Single(d => d.RuleName == "Argful").Reason,
                Is.EqualTo(ERuleDropReason.Unimplemented));
        }

        [Test]
        public void WarnDropped_RaisesStructuredAndStringChannels()
        {
            List<RuleDrop> drops = new List<RuleDrop>();
            List<string> warnings = new List<string>();
            void CaptureDrop(RuleDrop drop) => drops.Add(drop);
            void CaptureWarning(string message) => warnings.Add(message);

            RuleDiagnostics.OnRuleDropped += CaptureDrop;
            RuleDiagnostics.OnWarning += CaptureWarning;
            try
            {
                RuleDiagnostics.WarnDropped(new RuleDrop("Wolfborn", "unit 'Berserkers'",
                    ERuleDropReason.Unimplemented, "Skipping unimplemented special rule 'Wolfborn'."));
            }
            finally
            {
                RuleDiagnostics.OnRuleDropped -= CaptureDrop;
                RuleDiagnostics.OnWarning -= CaptureWarning;
            }

            Assert.That(drops, Has.Count.EqualTo(1));
            Assert.That(drops[0].RuleName, Is.EqualTo("Wolfborn"));
            Assert.That(drops[0].Reason, Is.EqualTo(ERuleDropReason.Unimplemented));
            Assert.That(warnings, Has.One.Contains("Wolfborn"),
                "string-channel subscribers (existing tests, the stdout fallback) must keep seeing drops.");
        }
    }
}
