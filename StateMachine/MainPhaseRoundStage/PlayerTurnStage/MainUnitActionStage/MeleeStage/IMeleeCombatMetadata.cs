
namespace FDG
{
    public interface IMeleeCombatMetadata : ICombatMetadata
    {
        //TODO: Register wounds dealt by each side?

    }

    public class MeleeCombatMetadata : IMeleeCombatMetadata
    {
        public ITextOutput TextOutput { get; }

        public IWeapon WeaponType { get; private set; }

        public int WeaponCount { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public EAttackType AttackType => EAttackType.Ranged;

        public IDiceRoller DiceRoller { get; }

        public bool IsSetUp => _hasSetWeapon;

        private bool _hasSetWeapon = false;

        private QueryableResults _queryableResults = new QueryableResults();

        public MeleeCombatMetadata(IUnit attackingUnit, IUnit defendingUnit, IDiceRoller diceRoller, ITextOutput textOutput)
        {
            DefendingUnit = defendingUnit;
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
}

