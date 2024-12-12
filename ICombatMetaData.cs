using System.Collections.Generic;

namespace FDG
{
    public interface ICombatMetadata
    {
        public IGameContext GameContext { get; }

        public IWeapon WeaponType { get; }

        public int WeaponCount { get; }

        public IUnit AttackingUnit { get; }

        public IUnit DefendingUnit { get; }

        public EAttackType AttackType { get; }

        IReadOnlyList<ISpecialRule_Combat> AllSpecialRules { get; }

        //TODO: Next value can replace everything after?
        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);
    }

    public enum EAttackType //TODO: Move?
    {
        Melee,
        Ranged
    }

    public static class ICombatMetaDataExtensions
    {
        public static IDiceRoller DiceRoller(this ICombatMetadata metadata)
        {
            return metadata.GameContext.DiceRoller;
        }

        public static ITextOutput TextOutput(this ICombatMetadata metadata)
        {
            return metadata.GameContext.TextOutput;
        }
    }
}