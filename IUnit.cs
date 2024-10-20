
namespace FDG
{
    public interface IUnit
    {
        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        /// <summary>
        /// How many wounds the unit had remaining when created.
        /// </summary>
        public int MaxWounds { get; }

        /// <summary>
        /// How many wounds remain before the unit is killed.
        /// </summary>
        public int RemainingWounds { get; }

        public List<IModel> Models { get; }

        public List<ISpecialRule_Combat> SpecialRules { get; }
    }

    public static class IUnitExtensions
    {
        public static List<IWeapon> AllWeapons(this IUnit unit)
        {
            List<IWeapon> allWeapons = new List<IWeapon>();

            foreach (IModel model in unit.Models)
            {
                allWeapons.AddRange(model.Weapons);
            }

            return allWeapons;
        }

        public static List<IWeapon> AllWeapons(this IUnit unit, Func<IWeapon, bool> predicate)
        {
            List<IWeapon> allWeapons = new List<IWeapon>();

            foreach (IModel model in unit.Models)
            {
                allWeapons.AddRange(model.Weapons.Where(predicate));
            }

            return allWeapons;
        }

        public static List<IWeapon> GetMeleeWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsMelee());
        }

        public static List<IWeapon> GetRangedWeapons(this IUnit unit)
        {
            return unit.AllWeapons(u => u.IsRanged());
        }
    }

    public class Unit : IUnit
    {
        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        public int MaxWounds
        {
            get
            {
                int total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds;
                }
                return total;
            }
        }

        public int RemainingWounds
        {
            get
            {
                int total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds - model.WoundsDealt;
                }
                return total;
            }
        }

        public List<IModel> Models { get; }

        public List<ISpecialRule_Combat> SpecialRules { get; }

        public Unit(string name, int quality, int defense, List<IModel> models, List<ISpecialRule_Combat> specialRules)
        {
            Name = name;
            Quality = quality;
            Defense = defense;
            Models = models;
            SpecialRules = specialRules;

        }
    }
}