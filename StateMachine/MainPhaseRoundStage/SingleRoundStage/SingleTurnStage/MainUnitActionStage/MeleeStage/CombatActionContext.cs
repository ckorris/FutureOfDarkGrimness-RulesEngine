using System.Collections.Concurrent;
using FDG.Data;

namespace FDG.Stages
{
    //TODO: There's lots of info that's specific to parts of the melee process.
    //Having a query handler like the combat metadata could be an improvement.
    public interface ICombatActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> AttackingUnit { get; }

        public DataBinding<UnitData> DefendingUnit { get; }

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons { get; }
        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons { get; }

        public IModel InRangeAttackingModels { get; }

        public IModel InRangeDefendingModels { get; }

        public Weapon? ShootingWeaponType { get; }

        public int? ShootingWeaponCount { get; }

        public float AttackerRemainingWoundsAtStart { get; }

        public float DefenderRemainingWoundsAtStart { get; }

        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);

        public void SetDefender(DataBinding<UnitData> defendingUnit);

        /// <summary>
        /// Distinct defender unit references this attacker has already engaged during the current action.
        /// Used by ranged shooting to enforce the 2-targets-per-shoot-action rule.
        /// </summary>
        public IReadOnlyCollection<DataReference> AttackedDefenderRefs { get; }

        /// <summary>
        /// Add a defender to <see cref="AttackedDefenderRefs"/>. Safe to call multiple times with the same defender.
        /// </summary>
        public void RegisterAttackedDefender(DataBinding<UnitData> defender);

        /// <summary>
        /// Swaps the attacker and defender roles for the rest of this melee (Counter: the charged unit
        /// strikes first, the charger strikes back). Rebuilds the available-weapon pool from the new
        /// attacker and clears the charging flag — the new (formerly defending) attacker is not charging.
        /// </summary>
        public void SwapCombatRoles();

        public void SetAttackWeapon(Weapon weaponToConsume, out int weaponCount);

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext);
    }

    public class CombatActionContext : ICombatActionContext
    {
        public DataBinding<UnitData> AttackingUnit { get; private set; }

        public DataBinding<UnitData> DefendingUnit { get; private set; }

        public IModel InRangeAttackingModels { get; private set; }

        public IModel InRangeDefendingModels { get; private set; }

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons => _alreadyUsedWeapons;

        public float AttackerRemainingWoundsAtStart { get; private set; }

        public float DefenderRemainingWoundsAtStart { get; private set; }

        public IGameContext GameContext { get; }

        public Weapon? ShootingWeaponType { get; private set; } = null;

        public int? ShootingWeaponCount { get; private set; } = null;

        private ConcurrentDictionary<Weapon, int> _availableWeapons;

        private ConcurrentDictionary<Weapon, int> _alreadyUsedWeapons = new ConcurrentDictionary<Weapon, int>();

        private HashSet<DataReference> _attackedDefenderRefs = new HashSet<DataReference>();

        public IReadOnlyCollection<DataReference> AttackedDefenderRefs => _attackedDefenderRefs;

        public void RegisterAttackedDefender(DataBinding<UnitData> defender)
        {
            _attackedDefenderRefs.Add(defender.Reference);
        }

        private QueryableResults _queryableResults = new QueryableResults();

        // Whether the attacking unit moved earlier this activation. Carried into each
        // CombatMetadata so hit-roll rules (Indirect) can read it; sourced from the
        // parent IUnitActionContext.HasMoved at child-context creation.
        private readonly bool _attackerMoved;

        // Whether this is the melee branch (vs. shooting). Carried into each CombatMetadata
        // so melee-only hit-roll rules (Furious) can gate on it; the hit-roll stages are shared.
        private readonly bool _isMelee;

        // Whether the attacking unit is charging (the charger's swing, not a strike-back).
        // Melee is only ever entered via Charge, so the charger's swing is charging; StrikeBackStage
        // builds its role-swapped context with isCharging:false. Carried into each CombatMetadata
        // so charge-only rules (Thrust) can gate on it. Cleared by SwapCombatRoles (a Counter swap makes
        // the formerly-defending unit the attacker, and it is not charging).
        private bool _isCharging;


        public CombatActionContext(IGameContext gameContext, DataBinding<UnitData> attackingUnit, bool isMelee,
            bool attackerMoved = false, bool isCharging = false)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            _attackerMoved = attackerMoved;
            _isMelee = isMelee;
            _isCharging = isCharging;
            if(isMelee)
            {
                _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetValue().GetMeleeWeapons());

            }
            else
            {
                _availableWeapons = GetTypeSortedWeapons(attackingUnit.GetValue().GetRangedWeapons());

            }

            AttackerRemainingWoundsAtStart = attackingUnit.GetValue().RemainingWounds;
        }

        public void AddResult<TResult>(TResult result)
        {
            _queryableResults.AddResult(result);
        }

        public bool QueryForResult<TResult>(out TResult result)
        {
            return _queryableResults.QueryForResult(out result);
        }

       
        public void SetAttackWeapon(Weapon weaponToConsume, out int weaponCount)
        {
            if (_availableWeapons.ContainsKey(weaponToConsume) == false)
            {
                throw new ArgumentException($"{nameof(CombatActionContext)}.{nameof(SetAttackWeapon)} called on weapon " +
                    $"that was not found in available list: {weaponToConsume.Name}");
            }

            _availableWeapons.TryRemove(weaponToConsume, out weaponCount);

            _alreadyUsedWeapons.TryAdd(weaponToConsume, weaponCount);

            ShootingWeaponType = weaponToConsume;
            ShootingWeaponCount = weaponCount;
        }

        public void SetDefender(DataBinding<UnitData> defendingUnit)
        {
            DefendingUnit = defendingUnit;
            DefenderRemainingWoundsAtStart = DefendingUnit.GetValue().RemainingWounds;
        }

        public void SwapCombatRoles()
        {
            (AttackingUnit, DefendingUnit) = (DefendingUnit, AttackingUnit);
            (AttackerRemainingWoundsAtStart, DefenderRemainingWoundsAtStart) =
                (DefenderRemainingWoundsAtStart, AttackerRemainingWoundsAtStart);

            // The new attacker (the Counter unit) is striking back into the charge, not charging.
            _isCharging = false;

            // Rebuild the melee-weapon pool from the new attacker; nothing has been used yet this swing.
            _availableWeapons = GetTypeSortedWeapons(AttackingUnit.GetValue().GetMeleeWeapons());
            _alreadyUsedWeapons.Clear();
        }

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext)
        {

            if(DefendingUnit == default || ShootingWeaponType == default || ShootingWeaponCount == default)
            {
                throw new InvalidOperationException($"Called {nameof(ConsumeAttackIntoContext)} when attack was not set up. " + 
                    "Must have all values set before consuming.");
            }

            CombatMetadata meleeCombatMetadata = new CombatMetadata(gameContext, AttackingUnit,
                DefendingUnit, ShootingWeaponType, ShootingWeaponCount.Value, _attackerMoved, _isMelee, _isCharging);

            // Don't clear DefendingUnit — OfferStrikeBackStage needs it after this call.
            ShootingWeaponType = null;
            ShootingWeaponCount = null;

            return meleeCombatMetadata;
        }

        //TODO: Repeated in Ranged version. Move to static class.
        private ConcurrentDictionary<Weapon, int> GetTypeSortedWeapons(List<Weapon> weapons)
        {
            ConcurrentDictionary<Weapon, int> weaponsAndCounts = new ConcurrentDictionary<Weapon, int>();

            WeaponComparer comparer = new WeaponComparer();

            foreach (Weapon newWeapon in weapons)
            {
                Weapon identicalWeapon = weaponsAndCounts.Keys.FirstOrDefault(keyWeapon => comparer.Equals(newWeapon, keyWeapon));

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

        /*
        private class PendingAttack 
        {
            public bool IsReady => DefendingUnit != default && WeaponType != default && WeaponCount != default;

            public DataBinding<UnitData> DefendingUnit = default;

            public Weapon WeaponType = default;

            public int WeaponCount = default;
        }
        */
    }


}