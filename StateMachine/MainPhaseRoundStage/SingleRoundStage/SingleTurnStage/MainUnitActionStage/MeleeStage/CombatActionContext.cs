using System.Collections.Concurrent;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// A defender engaged during the current action, with its remaining wounds captured the first time it
    /// was targeted. <see cref="ICombatActionContext.AttackedDefenders"/>.
    /// </summary>
    public readonly struct AttackedDefender
    {
        public DataBinding<UnitData> Defender { get; }
        public float RemainingWoundsAtStart { get; }

        public AttackedDefender(DataBinding<UnitData> defender, float remainingWoundsAtStart)
        {
            Defender = defender;
            RemainingWoundsAtStart = remainingWoundsAtStart;
        }
    }

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

        /// <summary>
        /// #197 P20: this shoot action is only legal because an enemy carries a Quick Shot mark ("friendly
        /// units get Quick Shot AGAINST it once"), so only marked units may be shot. Decided once by
        /// <c>ChooseActionStage</c>'s shoot gate - which is the only place that knows how far the unit
        /// moved - and carried here so <c>ChooseRangedAttackStage</c> narrows its target list the same way.
        /// Always false in melee and for any shot taken without rushing.
        /// </summary>
        public bool MarkedTargetsOnly { get; }

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons { get; }
        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons { get; }

        /// <summary>
        /// #315: weapons the player chose to HOLD FIRE with this action - out of
        /// <see cref="AvailableWeapons"/>, but deliberately NOT in <see cref="AlreadyUsedWeapons"/>, which
        /// means "has fired" and is what decides whether the action can still be backed out of and whether
        /// morale is owed. Declining is not firing.
        /// </summary>
        public IReadOnlyDictionary<Weapon, int> DeclinedWeapons { get; }

        /// <summary>
        /// #315: drop <paramref name="weaponToDecline"/> from this action's available pool without firing it
        /// (see <see cref="DeclinedWeapons"/>). Nothing is spent: a Limited weapon keeps its once-per-game
        /// shot, and a declined resolve-first weapon (Deadly/Takedown) stops gating the unit's other weapons.
        /// Throws if the weapon is not available, mirroring <see cref="SetAttackWeapon"/>.
        /// </summary>
        public void DeclineWeapon(Weapon weaponToDecline);

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

        /// <summary>#276: cap the just-queued attack's weapon count at <paramref name="maxCount"/> — the
        /// copies carried by models that can actually shoot the chosen target (range + line of sight).
        /// No-op unless exactly one attack is queued or when the cap wouldn't lower the count.</summary>
        public void TrimPendingAttack(int maxCount);

        /// <summary>Drop any queued attacks — the burst's target died mid-way, the leftover shots fizzle (#157).</summary>
        public void ClearPendingAttacks();

        public float AttackerRemainingWoundsAtStart { get; }

        public float DefenderRemainingWoundsAtStart { get; }

        /// <summary>
        /// Per-model remaining wounds captured at the start of the melee (both units), keyed by model
        /// reference. Empty for a ranged action. Lets a post-melee reflect rule (#197 P11 Retaliate /
        /// Deathstrike) measure exactly how many wounds each rule-bearing MODEL took, and which were killed,
        /// by comparing against the current state - the per-model attribution the owner ruled for.
        /// </summary>
        public IReadOnlyDictionary<DataReference, float> ModelRemainingWoundsAtStart { get; }

        public void AddResult<TResult>(TResult result);

        public bool QueryForResult<TResult>(out TResult result);

        public void SetDefender(DataBinding<UnitData> defendingUnit);

        /// <summary>
        /// Distinct defender unit references this attacker has already engaged during the current action.
        /// Used by ranged shooting to enforce the 2-targets-per-shoot-action rule.
        /// </summary>
        public IReadOnlyCollection<DataReference> AttackedDefenderRefs { get; }

        /// <summary>
        /// Every distinct defender engaged this action, in the order they were first targeted, each paired
        /// with its remaining wounds at the moment it was first targeted - i.e. before this attacker had
        /// dealt it any damage. Shooting resolves morale once per entry after the whole activation, so
        /// whether the action wounded the defender is measured against the wounds it started with, not
        /// against a mid-volley snapshot. See <c>ResolveRangedMoraleStage</c>.
        /// </summary>
        public IReadOnlyList<AttackedDefender> AttackedDefenders { get; }

        /// <summary>
        /// Add a defender to <see cref="AttackedDefenderRefs"/> and <see cref="AttackedDefenders"/>. Safe to
        /// call multiple times with the same defender; only the first call snapshots its wounds.
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

        public bool MarkedTargetsOnly { get; }

        public IReadOnlyList<DataBinding<ModelData>> InRangeAttackingModels { get; private set; }
            = new List<DataBinding<ModelData>>();

        public IReadOnlyList<DataBinding<ModelData>> InRangeDefendingModels { get; private set; }
            = new List<DataBinding<ModelData>>();

        public IReadOnlyDictionary<Weapon, int> AvailableWeapons => _availableWeapons;

        public IReadOnlyDictionary<Weapon, int> AlreadyUsedWeapons => _alreadyUsedWeapons;

        public IReadOnlyDictionary<Weapon, int> DeclinedWeapons => _declinedWeapons;

        public float AttackerRemainingWoundsAtStart { get; private set; }

        public float DefenderRemainingWoundsAtStart { get; private set; }

        public IReadOnlyDictionary<DataReference, float> ModelRemainingWoundsAtStart => _modelRemainingWoundsAtStart;
        private readonly Dictionary<DataReference, float> _modelRemainingWoundsAtStart = new();

        // Snapshots every model's remaining wounds (only in melee - reflect is a melee-only mechanic), keyed
        // by model reference so a Counter role-swap needs no re-keying. Called once per unit before any swing:
        // the attacker at construction, the defender when it is set.
        private void SnapshotModelWounds(DataBinding<UnitData> unit)
        {
            if (!_isMelee) return;
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                ModelData m = model.GetValue();
                _modelRemainingWoundsAtStart[model.Reference] = m.TotalWounds - m.WoundsDealt;
            }
        }

        public IGameContext GameContext { get; }

        // The attack(s) set up by SetAttackWeapon and consumed one per FireStage/SwingMeleeWeaponStage
        // entry. Normally holds a single (weapon, count) batch; SplitPendingAttackIntoSingleShots (#157)
        // turns a Takedown batch into N single-shot entries so each shot picks its own victim.
        // BurstIndex is the entry's position within that split burst (0 for unsplit attacks) — carried
        // into CombatMetadata so the attack beat can fire each shot from a different carrier (#276).
        private readonly Queue<(Weapon Weapon, int Count, int BurstIndex)> _pendingAttacks
            = new Queue<(Weapon, int, int)>();

        public bool HasPendingAttack => _pendingAttacks.Count > 0;

        private ConcurrentDictionary<Weapon, int> _availableWeapons;

        private ConcurrentDictionary<Weapon, int> _alreadyUsedWeapons = new ConcurrentDictionary<Weapon, int>();

        // #315: held-fire weapons. Kept apart from _alreadyUsedWeapons on purpose - see DeclinedWeapons.
        private ConcurrentDictionary<Weapon, int> _declinedWeapons = new ConcurrentDictionary<Weapon, int>();

        private HashSet<DataReference> _attackedDefenderRefs = new HashSet<DataReference>();

        private readonly List<AttackedDefender> _attackedDefenders = new List<AttackedDefender>();

        public IReadOnlyCollection<DataReference> AttackedDefenderRefs => _attackedDefenderRefs;

        public IReadOnlyList<AttackedDefender> AttackedDefenders => _attackedDefenders;

        public void RegisterAttackedDefender(DataBinding<UnitData> defender)
        {
            // Snapshot on first sight only. A later weapon aimed at the same unit must not re-baseline its
            // wounds, or the wounds-taken check would be measured against damage we already dealt.
            if (_attackedDefenderRefs.Add(defender.Reference))
                _attackedDefenders.Add(new AttackedDefender(defender, defender.GetValue().RemainingWounds));
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

        // #197: the activation context of the charging unit, consulted once the defender is known to recover
        // how far away that defender was before the charger moved. Null for shooting and for the strike-back
        // context (neither is a charge), and null on the resolver-less simulator paths.
        private readonly IUnitActionContext? _activationContext;

        // #197 (P15): the Unpredictable die, rolled once per attack ACTION and cached so every weapon of the
        // action shares one branch. Null until the first ConsumeAttackIntoContext resolves it; reset by
        // SwapCombatRoles so the new attacker (Counter) rolls its own branch. A die is consumed only when the
        // attacker actually carries an applicable Unpredictable rule (UnpredictableBranchResolver).
        private EUnpredictableBranch? _unpredictableBranch;


        public CombatActionContext(IGameContext gameContext, DataBinding<UnitData> attackingUnit, bool isMelee,
            bool attackerMoved = false, bool isCharging = false, IUnitActionContext? activationContext = null,
            bool markedTargetsOnly = false)
        {
            GameContext = gameContext;
            AttackingUnit = attackingUnit;
            ChargingUnit = isCharging ? attackingUnit : null;
            MarkedTargetsOnly = markedTargetsOnly;
            _attackerMoved = attackerMoved;
            _isMelee = isMelee;
            _isCharging = isCharging;
            _activationContext = activationContext;
            if(isMelee)
            {
                _availableWeapons = WeaponPool.GroupByProfile(attackingUnit.GetValue().GetMeleeWeapons());

            }
            else
            {
                _availableWeapons = WeaponPool.GroupByProfile(attackingUnit.GetValue().GetRangedWeapons());

            }

            AttackerRemainingWoundsAtStart = attackingUnit.GetValue().RemainingWounds;
            SnapshotModelWounds(attackingUnit);
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

            _pendingAttacks.Enqueue((weaponToConsume, weaponCount, 0));
        }

        public void DeclineWeapon(Weapon weaponToDecline)
        {
            if (_availableWeapons.ContainsKey(weaponToDecline) == false)
            {
                throw new ArgumentException($"{nameof(CombatActionContext)}.{nameof(DeclineWeapon)} called on weapon " +
                    $"that was not found in available list: {weaponToDecline.Name}");
            }

            _availableWeapons.TryRemove(weaponToDecline, out int weaponCount);

            _declinedWeapons.TryAdd(weaponToDecline, weaponCount);
        }

        public void SplitPendingAttackIntoSingleShots()
        {
            // Only ever called right after SetAttackWeapon, so exactly one batch is queued; anything else
            // means the caller is mid-burst and splitting would corrupt the queue.
            if (_pendingAttacks.Count != 1) return;

            (Weapon weapon, int count, _) = _pendingAttacks.Dequeue();
            for (int i = 0; i < count; i++)
            {
                _pendingAttacks.Enqueue((weapon, 1, i));
            }
        }

        public void TrimPendingAttack(int maxCount)
        {
            // Same single-batch window as the split above: only ever called right after SetAttackWeapon.
            if (_pendingAttacks.Count != 1 || maxCount <= 0) return;

            (Weapon weapon, int count, int burstIndex) = _pendingAttacks.Dequeue();
            _pendingAttacks.Enqueue((weapon, Math.Min(count, maxCount), burstIndex));
        }

        public void ClearPendingAttacks() => _pendingAttacks.Clear();

        public void SetDefender(DataBinding<UnitData> defendingUnit)
        {
            DefendingUnit = defendingUnit;
            DefenderRemainingWoundsAtStart = DefendingUnit.GetValue().RemainingWounds;
            SnapshotModelWounds(defendingUnit);
        }

        public void SetInRangeAttackers(IReadOnlyList<DataBinding<ModelData>> models)
        {
            InRangeAttackingModels = models;
            // Only models in melee range contribute weapons to the swing pool.
            _availableWeapons = WeaponPool.GroupByProfile(MeleeRangeUtilities.GetMeleeWeaponsFromModels(models));
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

            // The former defender is now the attacker; its own Unpredictable rule (if any) must roll fresh.
            _unpredictableBranch = null;

            // The in-range sets swap with the roles: the former defenders are now the attackers (#017).
            (InRangeAttackingModels, InRangeDefendingModels) = (InRangeDefendingModels, InRangeAttackingModels);

            // Rebuild the melee-weapon pool from the new attacker's in-range models; nothing used yet this swing.
            _availableWeapons = WeaponPool.GroupByProfile(MeleeRangeUtilities.GetMeleeWeaponsFromModels(InRangeAttackingModels));
            _alreadyUsedWeapons.Clear();
            _declinedWeapons.Clear();
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
            (Weapon weapon, int count, int burstIndex) = _pendingAttacks.Dequeue();

            return new CombatMetadata(gameContext, AttackingUnit,
                DefendingUnit, weapon, count, _attackerMoved, _isMelee, _isCharging,
                chargeOriginDistanceInches: ChargeOriginDistanceAgainstDefender(),
                unpredictableBranch: ResolveUnpredictableBranch(),
                burstShotIndex: burstIndex);
        }

        // #197 (P15): roll the Unpredictable die once per attack action, then reuse it for every weapon of
        // the action (cached). UnpredictableBranchResolver rolls only when the attacker carries an applicable
        // rule - or the DEFENDER carries an Unpredictable-granting mark, which the hit stage will claim onto
        // the attacker after this point - so a die is not consumed, and the seeded stream is not shifted,
        // for ordinary attacks.
        private EUnpredictableBranch ResolveUnpredictableBranch()
        {
            _unpredictableBranch ??= UnpredictableBranchResolver.Resolve(
                AttackingUnit.GetValue(), DefendingUnit.GetValue(), _isMelee, GameContext.DiceRoller);
            return _unpredictableBranch.Value;
        }

        // #197: only a charge has a launch distance, and only once the defender is pinned. A unit that was
        // absent from the activation-start snapshot (spawned mid-activation, or a simulator path with no
        // activation context) reports 0, which reads as "not launched from over N inches" for every gate.
        private float ChargeOriginDistanceAgainstDefender()
        {
            if (!_isCharging || _activationContext == null || DefendingUnit == default) return 0f;

            return _activationContext.TryGetActivationStartDistanceTo(
                DefendingUnit.GetValue().ID, out float distance) ? distance : 0f;
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