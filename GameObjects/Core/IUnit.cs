
using FDG.Data;
using FDG.Rules.Tokens;

namespace FDG
{
    public interface IUnit : IPlayerOwnable
    {
        /// <summary>
        /// Stable per-unit identifier, used by the rule system to track cross-unit
        /// token ownership and to re-link saved state on load. Assigned at unit
        /// creation; survives JSON / network round-trips.
        /// </summary>
        public UnitID ID { get; }

        /// <summary>
        /// Per-unit token container holding rule-system state (cost gates, status
        /// conditions, stacking markers, target tags, per-activation effects).
        /// Tokens survive JSON / network round-trips; subscriptions to the
        /// container's events do not and must be re-attached after rehydration.
        /// </summary>
        public ITokenContainer Tokens { get; }

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
                if (!model.GetIsAlive()) continue;
                allWeapons.AddRange(model.Weapons);
            }

            return allWeapons;
        }

        public static List<Weapon> AllWeapons(this IUnit unit, Func<Weapon, bool> predicate)
        {
            List<Weapon> allWeapons = new List<Weapon>();

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
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