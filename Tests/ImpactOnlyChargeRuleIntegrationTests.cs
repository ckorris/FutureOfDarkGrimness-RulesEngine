using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace FDG.Tests
{
    /// <summary>
    /// #345 — a unit with no melee weapon but an Impact-family rule may charge, and the melee it starts
    /// resolves in full. Impact(X), Heavy Impact(X) and Ravage(X) fire at
    /// <see cref="EHookID.Melee_OnChargeContact"/> — on the charge itself, not off a weapon — so gating the
    /// charge on carrying a melee weapon made them unreachable on the 138 corpus units (every APC, tank and
    /// speeder) whose only melee contribution IS the impact.
    ///
    /// <para>Two halves, tested here: the gate reads a rule's DECLARED hooks rather than a name list, and
    /// <c>DetermineInRangeAttackersStage</c> no longer sends a unit that reached contact with nothing to
    /// swing down the same exit as a unit that reached nobody at all.</para>
    /// </summary>
    [TestFixture]
    public class ImpactOnlyChargeRuleIntegrationTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester());
        }

        // ── The gate predicate: declared hooks, not names ────────────────────────────────────────

        [Test]
        public void AUnitWithNoMeleeWeapon_ButImpact_CanFightInMelee()
        {
            DataBinding<UnitData> tank = MakeUnit(RangedWeapon, new Position(10f, 0f));
            Attach(tank, CoreRuleCatalog.Impact, new RuleArgument.Int(3));

            Assert.That(ChargeContactRules.ActsOnChargeContact(tank.GetValue()), Is.True);
            Assert.That(ChargeContactRules.CanFightInMelee(tank.GetValue()), Is.True,
                "the impact IS its melee attack - this is what the charge gate asks.");
        }

        [Test]
        public void EveryImpactFamilyRule_Qualifies_ByItsHookNotItsName()
        {
            // Ravage and Heavy Impact are separate rules with separate effects; all three qualify because
            // they declare the same hook at the Actor seat. A name list would have to be maintained.
            foreach (SpecialRuleDefinition rule in new[] { CoreRuleCatalog.Impact, CoreRuleCatalog.Ravage })
            {
                DataBinding<UnitData> unit = MakeUnit(RangedWeapon, new Position(10f, 0f));
                Attach(unit, rule, new RuleArgument.Int(2));
                Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.True, rule.Name);
            }

            // The shipped Heavy Impact shape (#197): a supplement rule the engine has no name for.
            DataBinding<UnitData> rider = MakeUnit(RangedWeapon, new Position(10f, 0f));
            Attach(rider, HeavyImpactDefinition, new RuleArgument.Int(3));
            Assert.That(ChargeContactRules.CanFightInMelee(rider.GetValue()), Is.True,
                "a supplement rule authored at the charge-contact hook qualifies with no engine change.");
        }

        [Test]
        public void ADefensiveChargeContactRule_DoesNotLetItsBearerCharge()
        {
            // Counter also hooks Melee_OnChargeContact - but at the Subject seat, because reducing a
            // charger's impact dice is something you do when CHARGED. It must not read as a reason to charge.
            DataBinding<UnitData> unit = MakeUnit(RangedWeapon, new Position(10f, 0f));
            Attach(unit, CoreRuleCatalog.Counter);

            Assert.That(ChargeContactRules.ActsOnChargeContact(unit.GetValue()), Is.False,
                "a Subject-seat entry at the same hook is defensive - it is not a reason to charge.");
            Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.False);
        }

        [Test]
        public void AUnitWithNeitherWeaponNorChargeRule_StillCannotFight()
        {
            DataBinding<UnitData> unit = MakeUnit(RangedWeapon, new Position(10f, 0f));

            Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.False,
                "nothing to swing and nothing to trigger is still no charge.");
        }

        [Test]
        public void AMeleeWeaponAloneIsEnough()
        {
            DataBinding<UnitData> unit = MakeUnit(MeleeWeapon, new Position(10f, 0f));

            Assert.That(ChargeContactRules.ActsOnChargeContact(unit.GetValue()), Is.False);
            Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.True);
        }

        [Test]
        public void ADeadCarriersRule_DoesNotKeepTheUnitCharging()
        {
            // The rule rides a model; once that model is dead its declaration should not count.
            DataBinding<UnitData> unit = MakeUnit(RangedWeapon, new Position(10f, 0f));
            ModelData model = unit.GetValue().ModelBindings[0].GetValue();
            model.AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(3) }));

            Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.True,
                "a model-carried charge rule counts while its carrier lives.");

            model.DealWounds(model.TotalWounds);

            Assert.That(ChargeContactRules.CanFightInMelee(unit.GetValue()), Is.False,
                "a dead model's rule cannot deliver an impact.");
        }

        // ── The melee flow: contact with nothing to swing still resolves ─────────────────────────

        [Test]
        public void AnImpactOnlyChargerInContact_RoutesToTheStrikeBack_NotThePostMeleeFizzle()
        {
            // The whole point of the routing split: the charger has landed its impact hits and is now
            // locked with a living, armed defender. The defender is owed its strike-back.
            DataBinding<UnitData> tank = MakeUnit(RangedWeapon, new Position(11f, 0f));
            Attach(tank, CoreRuleCatalog.Impact, new RuleArgument.Int(3));
            DataBinding<UnitData> infantry = MakeUnit(MeleeWeapon, new Position(10f, 0f));

            (bool noneInRange, bool unarmedInContact, bool toWeaponChoice) = RunAttackersStage(tank, infantry);

            Assert.That(unarmedInContact, Is.True,
                "in contact with nothing to swing - the melee continues to the strike-back.");
            Assert.That(noneInRange, Is.False);
            Assert.That(toWeaponChoice, Is.False, "ChooseMeleeWeaponStage throws on an empty pool.");
        }

        [Test]
        public async Task TheDefenderRangeStage_SkipsTheWeaponOffer_ButStillRecordsWhoMayStrikeBack()
        {
            // DetermineInRangeDefendersStage owns the branch, because the strike-back needs the in-range
            // defenders IT records - routing around it would silently disarm the defender.
            DataBinding<UnitData> tank = MakeUnit(RangedWeapon, new Position(11f, 0f));
            Attach(tank, CoreRuleCatalog.Impact, new RuleArgument.Int(3));
            DataBinding<UnitData> infantry = MakeUnit(MeleeWeapon, new Position(10f, 0f));

            CombatActionContext context = new CombatActionContext(_ctx, tank, isMelee: true, isCharging: true);
            context.SetDefender(infantry);

            DetermineInRangeAttackersStage attackers =
                new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            attackers.ToDetermineDefenders.Bind("done");
            attackers.OnNoAttackersInRange.Bind("done");
            attackers.OnAttackersInRangeUnarmed.Bind("done");
            await attackers.Enter(context);

            bool toStrikeBack = false;
            bool toWeapons = false;
            DetermineInRangeDefendersStage defenders =
                new DetermineInRangeDefendersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            defenders.ToChooseMeleeWeapons.Bind("done");
            defenders.ToStrikeBackUnopposed.Bind("done");
            defenders.ToChooseMeleeWeapons.OnWillActivate += _ => toWeapons = true;
            defenders.ToStrikeBackUnopposed.OnWillActivate += _ => toStrikeBack = true;

            await defenders.Enter(context);

            Assert.That(toStrikeBack, Is.True, "no swing pool - go straight to the strike-back.");
            Assert.That(toWeapons, Is.False);
            Assert.That(context.InRangeDefendingModels, Has.Count.EqualTo(1),
                "the defender eligible to strike back must still have been recorded.");
        }

        [Test]
        public void AnArmedAttacker_StillTakesTheOrdinaryWeaponPath()
        {
            // Guards the split from over-reaching: nothing changes for a unit that CAN swing.
            DataBinding<UnitData> infantry = MakeUnit(MeleeWeapon, new Position(11f, 0f));
            DataBinding<UnitData> defender = MakeUnit(MeleeWeapon, new Position(10f, 0f));

            (bool noneInRange, bool unarmedInContact, bool toWeaponChoice) =
                RunAttackersStage(infantry, defender);

            Assert.That(toWeaponChoice, Is.True);
            Assert.That(unarmedInContact, Is.False);
            Assert.That(noneInRange, Is.False);
        }

        [Test]
        public void NobodyInContact_StillEndsTheMelee()
        {
            // The other half of the split: 20" apart, nobody reached contact, so there is no melee left to
            // resolve and no strike-back to offer.
            DataBinding<UnitData> tank = MakeUnit(RangedWeapon, new Position(30f, 0f));
            Attach(tank, CoreRuleCatalog.Impact, new RuleArgument.Int(3));
            DataBinding<UnitData> infantry = MakeUnit(MeleeWeapon, new Position(10f, 0f));

            (bool noneInRange, bool unarmedInContact, bool toWeaponChoice) = RunAttackersStage(tank, infantry);

            Assert.That(noneInRange, Is.True);
            Assert.That(unarmedInContact, Is.False);
            Assert.That(toWeaponChoice, Is.False);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────────

        private (bool NoneInRange, bool UnarmedInContact, bool ToWeaponChoice) RunAttackersStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            CombatActionContext context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            bool noneInRange = false, unarmedInContact = false, toWeaponChoice = false;

            DetermineInRangeAttackersStage stage =
                new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            stage.ToDetermineDefenders.Bind("done");
            stage.OnNoAttackersInRange.Bind("done");
            stage.OnAttackersInRangeUnarmed.Bind("done");
            stage.ToDetermineDefenders.OnWillActivate += _ => toWeaponChoice = true;
            stage.OnNoAttackersInRange.OnWillActivate += _ => noneInRange = true;
            stage.OnAttackersInRangeUnarmed.OnWillActivate += _ => unarmedInContact = true;

            stage.Enter(context).GetAwaiter().GetResult();

            return (noneInRange, unarmedInContact, toWeaponChoice);
        }

        // Mirrors the shipped GdfRuleSupplement.json entry: Impact(X) whose hits carry AP(1).
        private static readonly SpecialRuleDefinition HeavyImpactDefinition = new SpecialRuleDefinition(
            "Heavy Impact",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact,
                    new Condition.Always(),
                    new Effect.ChargeImpactHits(new ValueSource.Arg(0), ArmorPenetration: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition definition,
            params RuleArgument[] arguments) =>
            unit.GetValue().AttachRuleDefinition(
                new ResolvedRule(definition.Name, definition, arguments));

        private static Weapon RangedWeapon() => new Weapon("Shock Pistol", rangeInches: 12f, attacks: 1,
            armorPenetration: 0);

        private static Weapon MeleeWeapon() => new Weapon("CCW", rangeInches: 0f, attacks: 1,
            armorPenetration: 0);

        private DataBinding<UnitData> MakeUnit(Func<Weapon> weapon, Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon> { weapon() },
                initialPosition: position,
                gameDataStore: _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
