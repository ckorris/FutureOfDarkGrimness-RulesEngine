using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #027 slice 1: weapons are first-class rule carriers. Army-load resolves each
    // WeaponFileEntry's special-rule names against the registry and attaches the resolved
    // definitions to that weapon's instances — not to the unit, not to sibling weapons.
    // Scope is enforced in both directions at the load seam (ArmyListRuleResolution):
    // a weapon-scoped rule named at unit level, or a unit-scoped rule named on a weapon,
    // is skipped with a warning instead of attaching where it doesn't belong.
    [TestFixture]
    public class WeaponRuleAttachmentTests
    {
        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _player = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void WeaponRule_AttachesToEveryInstanceOfThatWeapon_AndNowhereElse()
        {
            UnitFileEntry entry = MakeUnitEntry(modelCount: 4,
                MakeWeaponEntry("Rifle", quantity: 2),
                MakeWeaponEntry("Big Gun", quantity: 2, new SpecialRuleEntry_CoreNumeric("Blast", 3)));

            var unit = new UnitData(_player, entry, _store, _resolver);

            List<IWeapon> bigGuns = unit.AllWeapons().Where(w => w.Name == "Big Gun").ToList();
            List<IWeapon> rifles = unit.AllWeapons().Where(w => w.Name == "Rifle").ToList();

            Assert.That(bigGuns, Has.Count.EqualTo(2));
            foreach (IWeapon bigGun in bigGuns)
            {
                Assert.That(bigGun.RuleDefinitions, Has.Count.EqualTo(1),
                    "Each Big Gun instance carries the Blast rule.");
                Assert.That(bigGun.RuleDefinitions[0].Definition, Is.SameAs(CoreRuleCatalog.Blast));
                Assert.That(bigGun.RuleDefinitions[0].Arguments, Has.Count.EqualTo(1),
                    "Blast(3)'s numeric value rides the attachment as an argument.");
            }

            foreach (IWeapon rifle in rifles)
            {
                Assert.That(rifle.RuleDefinitions, Is.Empty, "Sibling weapons don't inherit the rule.");
            }

            Assert.That(unit.RuleDefinitions, Is.Empty, "The unit itself doesn't inherit weapon rules.");
        }

        [Test]
        public void UnitScopedRuleNamedOnWeapon_IsSkipped()
        {
            UnitFileEntry entry = MakeUnitEntry(modelCount: 1,
                MakeWeaponEntry("Sneaky Gun", quantity: 1, new SpecialRuleEntry_Core("Stealth")));

            var unit = new UnitData(_player, entry, _store, _resolver);

            Assert.That(unit.AllWeapons().Single().RuleDefinitions, Is.Empty,
                "Stealth is unit-scoped and must not attach to a weapon.");
        }

        [Test]
        public void WeaponScopedRuleNamedAtUnitLevel_IsRefused()
        {
            ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                _resolver, new SpecialRuleEntry_CoreNumeric("Blast", 3), ERuleScope.Unit, "unit 'Misauthored'");

            Assert.That(resolved, Is.Null, "Blast is weapon-scoped and must not attach at unit level.");
        }

        [Test]
        public void UnitScopedRuleAtUnitLevel_StillResolves()
        {
            ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(
                _resolver, new SpecialRuleEntry_Core("Stealth"), ERuleScope.Unit, "unit 'Fine'");

            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.Definition, Is.SameAs(CoreRuleCatalog.Stealth));
        }

        [Test]
        public void AliasedWeaponRule_ResolvesWithRequestedNamePreserved()
        {
            var alias = new SpecialRuleEntry_Alias("Sunfire", new SpecialRuleEntry_CoreNumeric("Blast", 3));
            UnitFileEntry entry = MakeUnitEntry(modelCount: 1,
                MakeWeaponEntry("Sun Cannon", quantity: 1, alias));

            var unit = new UnitData(_player, entry, _store, _resolver);

            ResolvedRule rule = unit.AllWeapons().Single().RuleDefinitions.Single();
            Assert.That(rule.Definition, Is.SameAs(CoreRuleCatalog.Blast),
                "The alias resolves to the canonical definition (identity-based dispatch).");
            Assert.That(rule.RequestedName, Is.EqualTo(alias.PrintableName),
                "The army's own name survives for display.");
            Assert.That(rule.Arguments, Has.Count.EqualTo(1), "The aliased rule's argument is carried.");
        }

        [Test]
        public void NoResolver_YieldsNoWeaponRules()
        {
            UnitFileEntry entry = MakeUnitEntry(modelCount: 1,
                MakeWeaponEntry("Big Gun", quantity: 1, new SpecialRuleEntry_CoreNumeric("Blast", 3)));

            var unit = new UnitData(_player, entry, _store);

            Assert.That(unit.AllWeapons().Single().RuleDefinitions, Is.Empty,
                "Resolver-less construction paths load weapons without rules rather than throwing.");
        }

        private static UnitFileEntry MakeUnitEntry(int modelCount, params WeaponFileEntry[] weapons)
        {
            return new UnitFileEntry
            {
                Name = "Testers",
                ModelCount = modelCount,
                Quality = 4,
                Defense = 4,
                Weapons = weapons.ToList(),
            };
        }

        private static WeaponFileEntry MakeWeaponEntry(string name, int quantity, params SpecialRuleEntry[] rules)
        {
            return new WeaponFileEntry
            {
                Name = name,
                Quantity = quantity,
                RangeInches = 24,
                Attacks = 1,
                ArmorPenetration = 0,
                SpecialRules = rules.ToList(),
            };
        }
    }
}
