using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #441 F5 (#433 hand-off, signed off B): a unit carrying an Enhanced rule without the base rule its definition
    /// gates on gets one advisory line ("Warriors: Enhanced Lustbound does nothing without Lustbound") for the Army
    /// Builder's validation pane. The base is read off the definition's own UnitHasRule / WeaponHasRule gate - the
    /// evaluator's gate - never parsed from the name.
    /// </summary>
    [TestFixture]
    public class EnhancedRuleAuditTests
    {
        private static readonly IReadOnlyList<ActivatedAbility> NoAbilities = new List<ActivatedAbility>();

        /// <summary>The #433 Option B shape: an increment gated on the bearer having the base rule.</summary>
        private static SpecialRuleDefinition Enhanced(string name, Condition gate) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                        gate,
                        new Effect.MovementBonus(EActionType.Advance, 1f, 0f),
                        ELifetime.ThisActivation),
                },
                NoAbilities, Description: "If this unit has Lustbound, it moves further.");

        private static SpecialRuleDefinition Base(string name) =>
            new SpecialRuleDefinition(name,
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.Always(),
                        new Effect.MovementBonus(EActionType.Advance, 1f, 0f), ELifetime.ThisActivation),
                },
                NoAbilities, Description: "Moves further.");

        private static UnitFileEntry Unit(string name, params string[] rules)
        {
            UnitFileEntry unit = new UnitFileEntry { Name = name, Id = name, ModelCount = 5, Quality = 4, Defense = 4, PointCost = 100 };
            foreach (string rule in rules) unit.SpecialRules.Add(new SpecialRuleEntry_Core(rule));
            return unit;
        }

        private static ArmyListFile Army(params UnitFileEntry[] units)
        {
            ArmyListFile army = new ArmyListFile { Name = "Audit", PointsLimit = 1000, Units = units.ToList() };
            army.RuleDefinitions.Add(Base("Lustbound"));
            army.RuleDefinitions.Add(Enhanced("Enhanced Lustbound",
                new Condition.And(new Condition.ActionTypeIs(EActionType.Advance), new Condition.UnitHasRule("Lustbound"))));
            return army;
        }

        [Test]
        public void EnhancedWithoutItsBase_IsFlagged_OncePerUnit()
        {
            ArmyListFile army = Army(Unit("Warriors", "Enhanced Lustbound"), Unit("Riders", "Enhanced Lustbound", "Fearless"));

            Assert.That(EnhancedRuleAudit.Audit(army), Is.EqualTo(new[]
            {
                "Warriors: Enhanced Lustbound does nothing without Lustbound",
                "Riders: Enhanced Lustbound does nothing without Lustbound",
            }));
            Assert.That(EnhancedRuleAudit.Audit(army).All(l => l.All(c => c < 128)), Is.True, "game text is ASCII-only");
        }

        [Test]
        public void EnhancedWithItsBase_IsNotFlagged_WhereverTheBaseSits()
        {
            UnitFileEntry onUnit = Unit("Warriors", "Lustbound", "Enhanced Lustbound");

            UnitFileEntry onWeapon = Unit("Blades", "Enhanced Lustbound");
            onWeapon.Weapons.Add(new WeaponFileEntry { Name = "Blade", Quantity = 5, Attacks = 1,
                SpecialRules = { new SpecialRuleEntry_Core("Lustbound") } });

            UnitFileEntry onGrant = Unit("Mixed", "Enhanced Lustbound");
            onGrant.ModelGrants = new List<ModelGrantEntry>
                { new ModelGrantEntry { Count = 1, Rules = { new SpecialRuleEntry_Core("Lustbound") } } };

            UnitFileEntry numeric = Unit("Counted", "Enhanced Lustbound");
            numeric.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Lustbound", 3));

            Assert.That(EnhancedRuleAudit.Audit(Army(onUnit, onWeapon, onGrant, numeric)), Is.Empty);
        }

        [Test]
        public void AJoinedHeroSharesItsUnitsRules_BothWays()
        {
            UnitFileEntry squad = Unit("Squad", "Lustbound");
            UnitFileEntry hero = Unit("Champion", "Hero", "Enhanced Lustbound");
            hero.ModelCount = 1;
            hero.JoinsUnitId = squad.Id;

            UnitFileEntry squad2 = Unit("Second Squad", "Enhanced Lustbound");
            UnitFileEntry hero2 = Unit("Second Champion", "Hero", "Lustbound");
            hero2.ModelCount = 1;
            hero2.JoinsUnitId = squad2.Id;

            Assert.That(EnhancedRuleAudit.Audit(Army(squad, hero, squad2, hero2)), Is.Empty,
                "a joined Hero is one of the unit's models, so its rules count for UnitHasRule - and the unit's for it");
        }

        [Test]
        public void NoEnhancedRules_AnUnresolvableOne_AndOneWithoutAGate_SayNothing()
        {
            ArmyListFile army = Army(Unit("Plain", "Fearless"), Unit("Orphan", "Enhanced Nothing"), Unit("Aura", "Enhanced Lustbound Aura"));
            army.RuleDefinitions.Add(new SpecialRuleDefinition("Enhanced Lustbound Aura",
                new List<HookEntry>
                {
                    new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.Not(new Condition.UnitHasRule("Lustbound")),
                        new Effect.MovementBonus(EActionType.Advance, 1f, 0f), ELifetime.ThisActivation),
                },
                NoAbilities, Description: "Friendly units within 6in get Enhanced Lustbound."));

            Assert.That(EnhancedRuleAudit.Audit(army), Is.Empty,
                "no Enhanced rule, an unresolvable one (ArmyRuleAudit's drop), and a gate under Not are not dependencies");
        }

        [Test]
        public void BaseRuleNames_ReadTheDefinitionsGate_NotTheName()
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            resolver.Register(Enhanced("Enhanced Oddly Named",
                new Condition.Or(new Condition.UnitHasRule("Lustbound"), new Condition.WeaponHasRule("Parry"))));

            Assert.That(EnhancedRuleAudit.BaseRuleNames(resolver, "Enhanced Oddly Named"), Is.EqualTo(new[] { "Lustbound", "Parry" }));
            Assert.That(EnhancedRuleAudit.BaseRuleNames(resolver, "Enhanced Missing"), Is.Empty);
        }
    }
}
