
namespace FDG
{

    public interface IRangedCombatMetadata : ICombatMetadata
    {
        public void ChooseTarget(IUnit targetUnit);
    }

    public class RangedCombatMetadata : IRangedCombatMetadata
    {
        public ITextOutput TextOutput { get; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public EAttackType AttackType => EAttackType.Ranged;

        public IDiceRoller DiceRoller { get; }

        public bool IsSetUp => _hasSetWeapon && _hasSetTargetUnit;

        private bool _hasSetWeapon = false;
        private bool _hasSetTargetUnit = false;

        private QueryableResults _queryableResults = new QueryableResults();

        public RangedCombatMetadata(IUnit attackingUnit, IDiceRoller diceRoller, ITextOutput textOutput)
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

        public void ChooseTarget(IUnit targetUnit)
        {
            DefendingUnit = targetUnit;

            _hasSetTargetUnit = true;
        }
    }
}