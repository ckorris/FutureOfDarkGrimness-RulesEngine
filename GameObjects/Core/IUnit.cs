
using FDG.Data;

namespace FDG
{
    public interface IUnit : IPlayerOwnable
    {
        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        /// <summary>
        /// How many wounds the unit had remaining when created.
        /// </summary>
        public float MaxWounds { get; }

        /// <summary>
        /// How many wounds remain before the unit is killed.
        /// </summary>
        public float RemainingWounds { get; }

        public List<IModel> Models { get; }

        public List<ISpecialRule> SpecialRules { get; }

        public bool GetMobility(out float moveShootDistanceInches, out float chargeDistanceInches);

        public event DataValueChangedHandler<float> OnWoundsDealt;
    }

    
    public static class IUnitExtensions
    {
        public static bool GetIsAlive(this IUnit unit)
        {
            return unit.RemainingWounds > 0;
        }

        public static bool GetIsDead(this IUnit unit)
        {
            return unit.RemainingWounds <= 0;
        }

        public static List<IWeapon> AllWeapons(this IUnit unit)
        {
            List<IWeapon> allWeapons = new List<IWeapon>();

            foreach (IModel model in unit.Models)
            {
                allWeapons.AddRange(model.Weapons);
            }

            return allWeapons;
        }

        public static List<Weapon> AllWeapons(this IUnit unit, Func<Weapon, bool> predicate)
        {
            List<Weapon> allWeapons = new List<Weapon>();

            foreach (IModel model in unit.Models)
            {
                allWeapons.AddRange(model.Weapons.Where(predicate));
            }

            return allWeapons;
        }

        public static List<Weapon> GetMeleeWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsMelee());
        }

        public static List<Weapon> GetRangedWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsRanged());
        }

        public static List<ISpecialRule_Combat> GetCombatSpecialRules(this IUnit unit)
        {
            return unit.SpecialRules.OfType<ISpecialRule_Combat>().ToList();
        }

        public static List<ISpecialRule_Attacker> GetAttackerSpecialRules(this IUnit unit)
        {
            return unit.SpecialRules.OfType<ISpecialRule_Attacker>().ToList();
        }

        public static List<ISpecialRule_Defender> GetDefenderSpecialRules(this IUnit unit)
        {
            return unit.SpecialRules.OfType<ISpecialRule_Defender>().ToList();
        }

        public static List<ISpecialRule_Movement> GetMovementSpecialRules(this IUnit unit)
        {
            return unit.SpecialRules.OfType<ISpecialRule_Movement>().ToList();
        }
    }


}