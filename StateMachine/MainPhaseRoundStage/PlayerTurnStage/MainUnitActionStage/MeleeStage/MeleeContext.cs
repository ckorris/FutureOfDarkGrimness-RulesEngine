
using System.Collections.Concurrent;


namespace FDG.Stages
{
    //TODO: There's lots of info that's specific to parts of the melee process.
    //Having a query handler like the combat metadata could be an improvement.
    public interface IMeleeContext : IGameContextAccessor
    {
        public IUnit AttackingUnit { get; }

        public IUnit DefendingUnit { get; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons { get; }

        public IModel InRangeAttackingModels { get; }

        public IModel InRangeDefendingModels { get; }

        public IMeleeCombatMetadata MeleeCombatMetadata { get; }

        public float AttackerRemainingWoundsAtStart { get; }

        public float DefenderRemainingWoundsAtStart { get; }

        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);

        public void BeginNewAttack(IUnit attackingUnit, IUnit defendingUnit);

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount);

        public void ClearCurrentAttack();

        public void ResetMeleeCombatMetadata();
    }

    public class MeleeContext : IMeleeContext
    {
        public IGameContext GameContext { get; private set; }

        public IUnit AttackingUnit { get; private set; }

        public IUnit DefendingUnit { get; private set; }

        public IMeleeCombatMetadata MeleeCombatMetadata { get; private set; }

        public IModel InRangeAttackingModels { get; private set; }

        public IModel InRangeDefendingModels { get; private set; }

        public IReadOnlyDictionary<IWeapon, int> AvailableWeapons => _availableWeapons;

        public float AttackerRemainingWoundsAtStart { get; private set; }

        public float DefenderRemainingWoundsAtStart { get; private set; }

        private ConcurrentDictionary<IWeapon, int> _availableWeapons;

        private QueryableResults _queryableResults = new QueryableResults();


        public MeleeContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

        public void BeginNewAttack(IUnit attackingUnit, IUnit defendingUnit)
        {
            AttackingUnit = attackingUnit;
            DefendingUnit = defendingUnit;
            _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetMeleeWeapons());
            MeleeCombatMetadata = new MeleeCombatMetadata(attackingUnit, defendingUnit, 
                GameContext.DiceRoller, GameContext.TextOutput);
            AttackerRemainingWoundsAtStart = attackingUnit.RemainingWounds;
            DefenderRemainingWoundsAtStart = defendingUnit.RemainingWounds;
        }

        public void ChooseWeapon(IWeapon weaponToConsume, out int weaponCount)
        {
            if (_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(RangedContext)}.{nameof(ChooseWeapon)} called on weapon " +
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            MeleeCombatMetadata.ChooseWeapon(weaponToConsume, weaponCount);

        }

        public void ClearCurrentAttack()
        {
            AttackingUnit = null;
            _availableWeapons = null;
            MeleeCombatMetadata = default;
            InRangeAttackingModels = null;
            InRangeDefendingModels = null;
            _queryableResults.Reset();
        }

        public void ResetMeleeCombatMetadata()
        {
            MeleeCombatMetadata = new MeleeCombatMetadata(AttackingUnit, DefendingUnit, 
                GameContext.DiceRoller, GameContext.TextOutput);
        }

        //TODO: Repeated in Ranged version. Move to static class.
        private ConcurrentDictionary<IWeapon, int> GetTypeSortedWeapons(List<IWeapon> weapons)
        {
            ConcurrentDictionary<IWeapon, int> weaponsAndCounts = new ConcurrentDictionary<IWeapon, int>();

            WeaponComparer comparer = new WeaponComparer();

            foreach (IWeapon newWeapon in weapons)
            {
                IWeapon identicalWeapon = weaponsAndCounts.Keys.FirstOrDefault(keyWeapon => comparer.Equals(newWeapon, keyWeapon));

                if (identicalWeapon != default)
                {
                    weaponsAndCounts[identicalWeapon]++;
                }
                else
                {
                    weaponsAndCounts[newWeapon] = 1;
                }
            }

            return weaponsAndCounts;
        }
    }


}