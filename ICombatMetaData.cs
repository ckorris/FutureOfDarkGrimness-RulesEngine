

namespace FDG
{
    public interface ICombatMetaData
    {
        public ITextOutput TextOutput { get; }

        public IWeapon WeaponType { get; }

        public int WeaponCount { get; }

        public IUnit AttackingUnit { get; }

        public IUnit DefendingUnit { get; }

        public EAttackType AttackType { get; }

        //TODO: Next value can replace everything after?

        public IDiceRoller DiceRoller { get; }

        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);
    }

    public enum EAttackType //TODO: Move?
    {
        Melee,
        Ranged
    }
}