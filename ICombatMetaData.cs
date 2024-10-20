

namespace FDG
{
    public interface ICombatMetadata
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

        public bool IsSetUp { get; }

        public void ChooseWeapon(IWeapon weaponType, int weaponCount);
    }

    public enum EAttackType //TODO: Move?
    {
        Melee,
        Ranged
    }

    /* //I don't think I want to do this.
    public abstract class CombatMetadata : ICombatMetaData
    {
        public ITextOutput TextOutput { get; }

        public IWeapon WeaponType { get; protected set; }

        public int WeaponCount { get; protected set; }

        public IUnit AttackingUnit { get; protected set; }

        public IUnit DefendingUnit { get; protected set; }

        public EAttackType AttackType => EAttackType.Ranged;

        public IDiceRoller DiceRoller { get; }

        public bool IsSetUp => _hasSetWeapon && _hasSetTargetUnit;

        private bool _hasSetWeapon = false;
        private bool _hasSetTargetUnit = false;

        private QueryableResults _queryableResults = new QueryableResults();

        public CombatMetadata(IUnit attackingUnit, IDiceRoller diceRoller, ITextOutput textOutput)
        {
            AttackingUnit = attackingUnit;
            DiceRoller = diceRoller;
            TextOutput = textOutput;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        public void ChooseWeapon(IWeapon weaponType, int weaponCount)
        {
            WeaponType = weaponType;
            WeaponCount = weaponCount;

            _hasSetWeapon = true;
        }
    }
    */
}