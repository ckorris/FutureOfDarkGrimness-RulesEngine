using FDG.Data;
using Newtonsoft.Json;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.StageResolution.Requests
{
    public class ChooseRangedAttackRequest : IStageTaskRequest<CancellableResult<RangedAttackChoice>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> AttackingUnit { get; } //Not sure if needed, but can be helpful for the UI.

        public List<WeaponOption> WeaponOptions { get; }

        /// <summary>
        /// Whether the resolver should offer a Back button that abandons the shoot action (replying
        /// <see cref="Cancelled{T}"/>, which <c>ChooseRangedAttackStage</c> routes to Choose Action).
        /// True while NOTHING has fired yet this shoot action; false once a weapon has been committed,
        /// where there is no un-firing it and Cancelled has nowhere to return to.
        ///
        /// <para>Authoritative on the ENGINE side on purpose (#308). The GUI resolver used to track this
        /// itself with a per-attacker fire counter reset only when the attacking unit CHANGED — so a unit
        /// that shot once never saw Back again for the rest of the game, including on later activations.
        /// The stage already knows the answer (<c>AlreadyUsedWeapons</c>), so it says it.</para>
        ///
        /// <para>Mirrors <see cref="PlaceObjectsRequest{T}.AllowCancel"/>; the same name means the same
        /// thing in both.</para>
        /// </summary>
        public bool AllowCancel { get; }

        /// <summary>
        /// #319: whether the resolver should offer a "Done shooting" button that ENDS the shoot action
        /// (replying <see cref="Cancelled{T}"/>, which <c>ChooseRangedAttackStage</c> routes to morale and
        /// post-shoot, exactly as a shoot that ran out of targets does). True once a weapon HAS fired this
        /// action - the mirror image of <see cref="AllowCancel"/>, and exactly one of the two is ever true.
        ///
        /// <para>Both buttons reply <see cref="Cancelled{T}"/>; the engine decides what that means from
        /// <c>AlreadyUsedWeapons</c>, so a resolver can never route it wrongly. The pair exists so the button
        /// can be LABELLED truthfully - "Back" un-does nothing and returns to Choose Action, while "Done
        /// shooting" spends the action - which is the whole point of offering it (a unit used to be forced to
        /// fire every weapon that had a target, including a once-per-game Limited one).</para>
        /// </summary>
        public bool AllowStopShooting { get; }

        /// <summary>
        /// The unit the PREVIOUS weapon of this shoot action fired at, or null on the first weapon. A
        /// pre-selection hint only: a resolver should start with this target selected when the weapon it
        /// pre-selects can still legally fire at it, and is free to ignore it otherwise (#308 - a volley
        /// is usually aimed at one unit, and re-picking it per weapon was pure clicking).
        /// <para>Never a permission: the target's selectability is decided entirely by its
        /// <see cref="WeaponTargetStats"/>, as always.</para>
        /// </summary>
        public DataBinding<UnitData>? PreviousTarget { get; }

        [JsonConstructor]
        public ChooseRangedAttackRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions, bool allowCancel = true,
            DataBinding<UnitData>? previousTarget = null, bool allowStopShooting = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
            AllowCancel = allowCancel;
            PreviousTarget = previousTarget;
            AllowStopShooting = allowStopShooting;
        }

        public ChooseRangedAttackRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions, bool allowCancel = true,
            DataBinding<UnitData>? previousTarget = null, bool allowStopShooting = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
            AllowCancel = allowCancel;
            PreviousTarget = previousTarget;
            AllowStopShooting = allowStopShooting;
        }

        public Task<CancellableResult<RangedAttackChoice>> Resolve(CancellableResult<RangedAttackChoice> resolution)
        {
            return Task.FromResult(resolution);
        }

        /// <param name="IgnoresCover">True if this weapon ignores the target's cover (Blast). Resolvers
        /// should treat a target's <see cref="WeaponTargetStats.HasCover"/> as moot for this weapon when
        /// representing shootability.</param>
        /// <param name="IgnoresTerrain">True if this weapon ignores intervening terrain for line of sight
        /// (Indirect, Takedown), so it can fire at targets out of line of sight.</param>
        /// <param name="CoverIgnoreRule">Display name of the rule causing <paramref name="IgnoresCover"/>
        /// (alias-aware), for player-facing attribution like "(Blast ignores cover)"; null when none.</param>
        /// <param name="LineOfSightIgnoreRule">Display name of the rule causing <paramref name="IgnoresTerrain"/>,
        /// for attribution like "(Indirect ignores line of sight)"; null when none.</param>
        /// <param name="LimitedRule">#319. Display name of the once-per-game rule this weapon carries
        /// ("Limited", alias-aware), or null when it has none. Set whether or not the weapon is still
        /// available: a SPENT one arrives with its targets already unselectable ("Already fired (Limited)"),
        /// an unspent one is about to be committed forever the moment the player fires it, and resolvers must
        /// say so in both states. Carried on the option rather than read off the weapon because
        /// <see cref="IWeapon.RuleDefinitions"/> is <c>[JsonIgnore]</c> - a remote player's copy of this
        /// request has no rules on it at all.</param>
        /// <param name="LimitedAlreadyFired">#319. True when <paramref name="LimitedRule"/> is set AND the
        /// weapon has already been fired this game, so it can never fire again (its targets arrive
        /// unselectable). Lets a resolver tell "spent" from "about to be spent" without parsing the
        /// unselectable reason back into a meaning.</param>
        public record WeaponOption(Weapon Weapon, List<WeaponTargetStats> WeaponTargetStats,
            bool IgnoresCover = false, bool IgnoresTerrain = false,
            string? CoverIgnoreRule = null, string? LineOfSightIgnoreRule = null,
            string? LimitedRule = null, bool LimitedAlreadyFired = false);

        /// <summary>
        /// List which models can and cannot shoot at a given unit, of the models in a unit that have a specific weapon.
        /// Lists should not include models that don't have the weapon in question.
        /// </summary>
        /// <param name="TargetUnit">Unit being targeted.</param>
        /// <param name="modelsThatCanShoot">Models with the weapon that can hit (have line of sight and range)</param>
        /// <param name="UnselectableReason">If non-null, the target is unselectable even when it has shooters in range
        /// (e.g. the attacker has already targeted the maximum number of distinct units this shoot action). UIs should
        /// list the target as disabled and surface the reason as a tooltip.</param>
        /// <param name="Forecast">#323. The pre-roll forecast for firing THIS weapon at THIS target -
        /// effective thresholds plus the modifier chips behind them - computed engine-side by
        /// <c>ShootingForecast</c> (a resolver cannot compute it: weapon rules never cross the wire).
        /// Null for a row with no shooters in range (nothing to price).</param>
        public record WeaponTargetStats(DataBinding<UnitData> TargetUnit, HashSet<DataBinding<ModelData>> modelsThatCanShoot,
            HashSet<DataBinding<ModelData>> modelsWithWeaponThatCannotShoot, bool HasCover = false,
            string? UnselectableReason = null, AttackForecast? Forecast = null);

        /// <summary>
        /// #323: what the dice will actually ask for if this weapon fires at this target - the numbers a
        /// player needs to COMPARE targets before committing, shown in the targeting UI. Thresholds are
        /// display-clamped to the 2-6 band (a natural 6 always hits, a natural 1 always fails), exactly as
        /// the roll stages clamp before rolling.
        /// </summary>
        /// <param name="HitRollNeeded">Effective to-hit threshold: attacker Quality after floors
        /// (Reliable), roll modifiers (Stealth, Precise...), granted buffs and persistent markers.</param>
        /// <param name="SaveRollNeeded">Effective save threshold: defender Defense after weapon AP
        /// (Fortified-reduced), cover, whole-attack save modifiers (Shielded), granted buffs and
        /// persistent markers.</param>
        /// <param name="HitTags">The arithmetic behind <paramref name="HitRollNeeded"/> as display chips
        /// ("Quality 4+", "Stealth -1") - the SAME strings the post-roll dice beat shows, composed by the
        /// same code. Null when the threshold is just the unmodified Quality.</param>
        /// <param name="SaveTags">The arithmetic behind <paramref name="SaveRollNeeded"/> ("Defense 4+",
        /// "AP 2", "Cover +1"). Null when the threshold is just the unmodified Defense.</param>
        /// <param name="Notes">Roll-time facts the forecast cannot price - spendable markers the attacker
        /// may claim mid-roll, an unclaimed Mark on the target. Null when there are none. Face-triggered
        /// effects (Rending, Furious) are deliberately absent: they already read as rule names on the
        /// weapon line, and pricing them pre-roll would be a guess.</param>
        public record AttackForecast(int HitRollNeeded, int SaveRollNeeded,
            List<string>? HitTags = null, List<string>? SaveTags = null, List<string>? Notes = null);

        /// <summary>
        /// Record suited to choosing your attack, with the attacking unit implied.
        /// </summary>
        /// <param name="Weapon">Weapon used to shoot.</param>
        /// <param name="TargetUnit">Unit to target, or null to HOLD FIRE with this weapon (#319) - see
        /// <see cref="HoldFire"/>.</param>
        public record RangedAttackChoice(Weapon Weapon, DataBinding<UnitData>? TargetUnit)
        {
            /// <summary>
            /// #319: "don't shoot this weapon at all this action". The weapon leaves the action's available
            /// pool without firing - so it is never marked spent (the point, for a once-per-game Limited
            /// weapon), and it stops gating the unit's other weapons (a Deadly/Takedown weapon you decline
            /// must not go on demanding to be resolved first). The rest of the unit's weapons are then
            /// offered as usual.
            /// <para>A null target is the wire form because the reply type is fixed at
            /// <c>CancellableResult&lt;RangedAttackChoice&gt;</c>: a third
            /// <see cref="CancellableResult{T}"/> subtype would have to be generic over T for the benefit of
            /// one request, while a weapon with nothing to shoot at says exactly this.</para>
            /// </summary>
            public static RangedAttackChoice HoldFire(Weapon weapon) => new(weapon, null);

            /// <summary>True when this reply declines to fire <see cref="Weapon"/> (see <see cref="HoldFire"/>).</summary>
            [JsonIgnore] public bool IsHoldFire => TargetUnit == null;
        }
    }
}
