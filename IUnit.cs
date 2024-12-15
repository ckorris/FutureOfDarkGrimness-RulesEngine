using System;
using System.Linq;
using System.Collections.Generic;

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

        public event WoundsDealtEventHandler OnWoundsDealt;
    }

    public class Unit : IUnit
    {
        public PlayerID PlayerID { get; private set; }

        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        public float MaxWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds;
                }
                return total;
            }
        }

        public float RemainingWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds - model.WoundsDealt;
                }
                return total;
            }
        }

        public List<IModel> Models { get; }

        public List<ISpecialRule> SpecialRules { get; }

        public Unit(PlayerID playerID, string name, int quality, int defense, List<IModel> models,
            List<ISpecialRule> specialRules)
        {
            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;
            Models = models;
            SpecialRules = specialRules;

            foreach (IModel model in models)
            {
                model.OnWoundsDealt += OnModelWoundsDealt;
            }

        }

        public event WoundsDealtEventHandler OnWoundsDealt;

        private void OnModelWoundsDealt(WoundsDealtEventArgs modelWoundsDealtArgs)
        {
            OnWoundsDealt?.Invoke(new WoundsDealtEventArgs(modelWoundsDealtArgs.WoundsDealt, RemainingWounds, this.GetIsDead()));
        }

        public bool GetMobility(out float moveShootDistanceInches, out float chargeDistanceInches)
        {
            //TODO: Process special rules for this.
            moveShootDistanceInches = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES;
            chargeDistanceInches = GameWideConstants.CHARGE_DISTANCE_INCHES;

            return true;
        }

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