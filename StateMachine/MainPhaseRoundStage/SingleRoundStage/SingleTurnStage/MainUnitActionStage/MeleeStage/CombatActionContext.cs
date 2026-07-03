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

        /// <summary>
        /// The unit that charged into this melee, captured at context creation and never reassigned by
        /// <see cref="SwapCombatRoles"/>. Null for non-charging contexts (shooting, strike-back). #020:
        /// the charger is Fatigued for charging even when a Counter unit denies it a swing.
        /// </summary>
        public DataBinding<UnitData>? ChargingUnit { get; }

        /// <summary>
        /// True once the defender has actually struck back in this melee. #020: a unit that strikes back
        /// becomes Fatigued, so <c>ApplyFatigueStage</c> reads this to decide whether to fatigue it.
        /// </summary>
        public bool DefenderStruckBack { get; }

        /// <summary>Record that the defender struck back this melee. Idempotent.</summary>
        public void RegisterDefenderStruckBack();

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons { get; }
        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons { get; }

        public IReadOnlyList<DataBinding<ModelData>> InRangeAttackingModels { get; }

        public IReadOnlyList<DataBinding<ModelData>> InRangeDefendingModels { get; }

        /// <summary>
        /// Restricts the attacking models that may strike to <paramref name="models"/> and rebuilds the
        /// available melee-weapon pool from only their weapons (#017). Called by
        /// <see cref="DetermineInRangeAttackersStage"/> after pile-in settles positions.
        /// </summary>
        public void SetInRangeAttackers(IReadOnlyList<DataBinding<ModelData>> models);

        /// <summary>
        /// Records the defending models that may strike back (#017). Called by
        /// <see cref="DetermineInRangeDefendersStage"/>; consumed when the roles swap on strike-back.
        /// </summary>
        public void SetInRangeDefenders(IReadOnlyList<DataBinding<ModelData>> models);

        /// <summary>True while at least one queued attack awaits firing. Normally one per
        /// <see cref="SetAttackWeapon"/>; a Takedown-split weapon queues one single-shot attack per copy
        /// (#157) and FireStage loops until the queue drains.</summary>
        public bool HasPendingAttack { get; }

        /// <summary>#157: split the just-queued attack (weapon, N) into N single-shot attacks so each shot
        /// picks its own individual target (Takedown / Sniper). No-op unless exactly one attack is queued.</summary>
        public void SplitPendingAttackIntoSingleShots();

        /// <summary>Drop any queued attacks — the burst's target died mid-way, the leftover shots fizzle (#157).</summary>
        public void ClearPendingAttacks();

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

        // Immutable capture of the charging unit (#020). Set once at construction from the initial
        // attacker when isCharging; SwapCombatRoles (Counter) deliberately leaves it alone so the
        // charger is recoverable even after the roles flip.
        public DataBinding<UnitData>? ChargingUnit { get; }

        public bool DefenderStruckBack { get; private set; }

        public void RegisterDefenderStruckBack() => DefenderStruckBack = true;

        public IReadOnlyList<DataBinding<ModelData>> InRangeAttackingModels { get; private set; }
            = new List<DataBinding<ModelData>>();

        public IReadOnlyList<DataBinding<ModelData>> InRangeDefendingModels { get; private set; }
            = new List<DataBinding<ModelData>>();

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons => _alreadyUsedWeapons;

        public float AttackerRemainingWoundsAtStart { get; private set; }

        public float DefenderRemainingWoundsAtStart { get; private set; }

        public IGameContext GameContext { get; }

        // The attack(s) set up by SetAttackWeapon and consumed one per FireStage/SwingMeleeWeaponStage
        // entry. Normally holds a single (weapon, count) batch; SplitPendingAttackIntoSingleShots (#157)
        // turns a Takedown batch into N single-shot entries so each shot picks its own victim.
        private readonly Queue<(Weapon Weapon, int Count)> _pendingAttacks = new Queue<(Weapon, int)>();

        public bool HasPendingAttack => _pendingAttacks.Count > 0;

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
            ChargingUnit = isCharging ? attackingUnit : null;
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

            _pendingAttacks.Enqueue((weaponToConsume, weaponCount));
        }

        public void SplitPendingAttackIntoSingleShots()
        {
            // Only ever called right after SetAttackWeapon, so exactly one batch is queued; anything else
            // means the caller is mid-burst and splitting would corrupt the queue.
            if (_pendingAttacks.Count != 1) return;

            (Weapon weapon, int count) = _pendingAttacks.Dequeue();
            for (int i = 0; i < count; i++)
            {
                _pendingAttacks.Enqueue((weapon, 1));
            }
        }

        public void ClearPendingAttacks() => _pendingAttacks.Clear();

        public void SetDefender(DataBinding<UnitData> defendingUnit)
        {
            DefendingUnit = defendingUnit;
            DefenderRemainingWoundsAtStart = DefendingUnit.GetValue().RemainingWounds;
        }

        public void SetInRangeAttackers(IReadOnlyList<DataBinding<ModelData>> models)
        {
            InRangeAttackingModels = models;
            // Only models in melee range contribute weapons to the swing pool.
            _availableWeapons = GetTypeSortedWeapons(MeleeRangeUtilities.GetMeleeWeaponsFromModels(models));
        }

        public void SetInRangeDefenders(IReadOnlyList<DataBinding<ModelData>> models)
        {
            InRangeDefendingModels = models;
        }

        public void SwapCombatRoles()
        {
            (AttackingUnit, DefendingUnit) = (DefendingUnit, AttackingUnit);
            (AttackerRemainingWoundsAtStart, DefenderRemainingWoundsAtStart) =
                (DefenderRemainingWoundsAtStart, AttackerRemainingWoundsAtStart);

            // The new attacker (the Counter unit) is striking back into the charge, not charging.
            _isCharging = false;

            // The in-range sets swap with the roles: the former defenders are now the attackers (#017).
            (InRangeAttackingModels, InRangeDefendingModels) = (InRangeDefendingModels, InRangeAttackingModels);

            // Rebuild the melee-weapon pool from the new attacker's in-range models; nothing used yet this swing.
            _availableWeapons = GetTypeSortedWeapons(MeleeRangeUtilities.GetMeleeWeaponsFromModels(InRangeAttackingModels));
            _alreadyUsedWeapons.Clear();
            _pendingAttacks.Clear();
        }

        public ICombatMetadata ConsumeAttackIntoContext(IGameContext gameContext)
        {
            if (DefendingUnit == default || _pendingAttacks.Count == 0)
            {
                throw new InvalidOperationException($"Called {nameof(ConsumeAttackIntoContext)} when attack was not set up. " +
                    "Must have all values set before consuming.");
            }

            // Don't clear DefendingUnit — OfferStrikeBackStage needs it after this call.
            (Weapon weapon, int count) = _pendingAttacks.Dequeue();

            return new CombatMetadata(gameContext, AttackingUnit,
                DefendingUnit, weapon, count, _attackerMoved, _isMelee, _isCharging);
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