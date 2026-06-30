using System;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    // #032 Limited — the spent-state helper. The "fired this game" mark is a per-MODEL token keyed by weapon
    // name, so two carriers spend independently, a model can hold two different Limited weapons without
    // confusing them, and a casualty drops its mark with it.
    [TestFixture]
    public class LimitedRulesTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void IsLimited_TrueForLimitedWeapon_FalseForPlainWeapon()
        {
            Assert.That(LimitedRules.IsLimited(LimitedWeapon("Rocket")), Is.True);
            Assert.That(LimitedRules.IsLimited(new Weapon("Rifle", 24f, 1, 0)), Is.False);
        }

        [Test]
        public void MarkFired_FlipsIsSpent_ForALimitedWeapon()
        {
            Weapon rocket = LimitedWeapon("Rocket");
            DataBinding<UnitData> unit = MakeUnit(MakeModel(rocket));

            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.False, "available before firing.");
            LimitedRules.MarkFired(unit.GetValue(), rocket);
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.True, "spent after firing.");
        }

        [Test]
        public void IsSpent_OnlyWhenEveryLivingCarrierHasFired()
        {
            // Two models, each carrying their own Rocket (same name). One model having fired doesn't spend the
            // weapon for the unit — the other carrier still has its shot; only when all have fired is it spent.
            DataBinding<ModelData> a = MakeModel(LimitedWeapon("Rocket"));
            DataBinding<ModelData> b = MakeModel(LimitedWeapon("Rocket"));
            DataBinding<UnitData> unit = MakeUnit(a, b);
            Weapon rocket = a.GetValue().Weapons[0];

            AddSpentToken(a, "Rocket"); // only model A has fired
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.False,
                "model B still has its Rocket — not spent for the unit.");

            LimitedRules.MarkFired(unit.GetValue(), rocket); // marks every remaining living carrier
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.True);
        }

        [Test]
        public void SpentState_IsKeyedByWeaponName()
        {
            // One model with two DIFFERENT Limited weapons; firing one must not spend the other.
            DataBinding<ModelData> model = MakeModel(LimitedWeapon("Rocket"), LimitedWeapon("Grenade"));
            DataBinding<UnitData> unit = MakeUnit(model);
            Weapon rocket = model.GetValue().Weapons.First(w => w.Name == "Rocket");
            Weapon grenade = model.GetValue().Weapons.First(w => w.Name == "Grenade");

            LimitedRules.MarkFired(unit.GetValue(), rocket);

            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.True);
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), grenade), Is.False,
                "the model's other Limited weapon is independent (token keyed by weapon name).");
        }

        [Test]
        public void IsSpent_ConsidersLivingCarriersOnly()
        {
            DataBinding<ModelData> a = MakeModel(LimitedWeapon("Rocket"));
            DataBinding<ModelData> b = MakeModel(LimitedWeapon("Rocket"));
            DataBinding<UnitData> unit = MakeUnit(a, b);
            Weapon rocket = a.GetValue().Weapons[0];

            AddSpentToken(a, "Rocket"); // only A has fired; B is alive + unspent → not spent
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.False);

            b.GetValue().DealWounds(b.GetValue().TotalWounds); // the only unspent carrier dies
            Assert.That(LimitedRules.IsSpent(unit.GetValue(), rocket), Is.True,
                "with the unspent carrier dead, every living carrier has fired.");
        }

        private static Weapon LimitedWeapon(string name, float range = 24f)
        {
            var weapon = new Weapon(name, range, 1, 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Limited", CoreRuleCatalog.Limited));
            return weapon;
        }

        private DataBinding<ModelData> MakeModel(params Weapon[] weapons)
        {
            var model = new ModelData(0.5f, weapons.ToList(), new Position(0, 0, 0), _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Bomber", quality: 4, defense: 4,
                modelBindings: models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void AddSpentToken(DataBinding<ModelData> model, string weaponName) =>
            model.GetValue().Tokens.AddToken(new Token(TokenType.LimitedSpent, 1,
                new TokenClearTrigger.ManualOnly(), new TokenPayload.WeaponName(weaponName)));
    }
}
