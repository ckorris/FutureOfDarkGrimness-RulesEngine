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
            DataBinding<UnitData>? previousTarget = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
            AllowCancel = allowCancel;
            PreviousTarget = previousTarget;
        }

        public ChooseRangedAttackRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> attackingUnit, List<WeaponOption> weaponOptions, bool allowCancel = true,
            DataBinding<UnitData>? previousTarget = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            AttackingUnit = attackingUnit;
            WeaponOptions = weaponOptions;
            AllowCancel = allowCancel;
            PreviousTarget = previousTarget;
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
        public record WeaponOption(Weapon Weapon, List<WeaponTargetStats> WeaponTargetStats,
            bool IgnoresCover = false, bool IgnoresTerrain = false,
            string? CoverIgnoreRule = null, string? LineOfSightIgnoreRule = null);

        /// <summary>
        /// List which models can and cannot shoot at a given unit, of the models in a unit that have a specific weapon.
        /// Lists should not include models that don't have the weapon in question.
        /// </summary>
        /// <param name="TargetUnit">Unit being targeted.</param>
        /// <param name="modelsThatCanShoot">Models with the weapon that can hit (have line of sight and range)</param>
        /// <param name="UnselectableReason">If non-null, the target is unselectable even when it has shooters in range
        /// (e.g. the attacker has already targeted the maximum number of distinct units this shoot action). UIs should
        /// list the target as disabled and surface the reason as a tooltip.</param>
        public record WeaponTargetStats(DataBinding<UnitData> TargetUnit, HashSet<DataBinding<ModelData>> modelsThatCanShoot,
            HashSet<DataBinding<ModelData>> modelsWithWeaponThatCannotShoot, bool HasCover = false,
            string? UnselectableReason = null);

        /// <summary>
        /// Record suited to choosing your attack, with the attacking unit implied.
        /// </summary>
        /// <param name="Weapon">Weapon used to shoot.</param>
        /// <param name="TargetUnit">Unit to target.</param>
        public record RangedAttackChoice(Weapon Weapon, DataBinding<UnitData> TargetUnit);
    }
}
