using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class DefineMovementPathRequest : IStageTaskRequest<CancellableResult<List<ModelMoveEntry>>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitDataBinding { get; }

        public float MaxAdvanceDistance { get; }
        public float MaxRushDistance { get; }
        public float MaxDistanceInches { get; }

        /// <summary>
        /// Per-model move budgets (#093), one entry per living model whose budget the resolvers should honour
        /// — a joined hero's Fast/Slow gives it a different Advance/Rush/hard-cap than the rest of the unit.
        /// The scalars above are the unit-wide default; a resolver looks a model up here and falls back to
        /// them when it's absent (see <see cref="BudgetFor"/>). Empty when the unit has no per-model movement
        /// rules, so every model uses the unit scalars — identical to the pre-#093 request.
        /// </summary>
        public IReadOnlyList<ModelMoveBudgetInfo> ModelMoveBudgets { get; }

        /// <summary>
        /// Per-weapon sighting rules for the moving unit's ranged weapons — whether each ignores cover
        /// (Blast) or intervening terrain (Indirect). The movement resolver doesn't consume this yet, but
        /// it's the info needed to represent "after I move here, can I shoot that" (a cover-/terrain-blocked
        /// target may still be shootable with an ignoring weapon). Empty when the unit has no ranged weapons.
        /// </summary>
        public IReadOnlyList<WeaponSightProfile> WeaponSightProfiles { get; }

        /// <summary>
        /// Whether the moving unit may path through enemy bases (Strafing's fly-over). Derived from the
        /// unit's #042 rules where the request is built, and read by the resolvers so their move-preview
        /// validation agrees with the authoritative stage check. It still may not END on an enemy.
        /// </summary>
        public bool CanMoveThroughEnemies { get; }

        /// <summary>
        /// Whether the moving unit ignores the difficult-terrain move cap (Strider). Derived from the unit's
        /// #042 rules where the request is built, and read by the resolvers so their move-preview validation
        /// agrees with the authoritative stage check (a Strider unit may move full distance across Difficult
        /// terrain without being limited to the difficult-terrain cap).
        /// </summary>
        public bool IgnoresDifficultTerrain { get; }

        /// <summary>
        /// Whether the moving unit also ignores impassible terrain — flies over it (Flying's AllTerrain scope,
        /// on top of <see cref="IgnoresDifficultTerrain"/>). Derived from the unit's rules where the request is
        /// built; read by the resolvers so their move-preview validation agrees with the authoritative stage
        /// check. The unit still may not end stacked on an enemy.
        /// </summary>
        public bool IgnoresImpassibleTerrain { get; }

        /// <summary>
        /// Per-(weapon, enemy unit) effective shooting range where a range-modifier rule changes it
        /// (Increased Shooting Range widens the mover's reach; Ranged Shrouding narrows it for that enemy) —
        /// precomputed engine-side via <see cref="Rules.Dispatch.RangeRuleQueries.EffectiveRange"/> so the
        /// movement "Show targeting" overlay can preview post-move shooting reach in step with the
        /// authoritative <c>ChooseRangedAttackStage</c> check. Only entries that differ from the weapon's base
        /// range are listed; a missing (weapon, enemy) pair means "use <c>weapon.RangeInches</c>". Empty when
        /// no range rule is in play.
        /// </summary>
        public IReadOnlyList<WeaponRangeOverride> WeaponRangeOverrides { get; }

        /// <summary>
        /// Whether the resolver should offer a Back button that abandons the whole move. True for the Move
        /// action, whose stage returns to the action menu without spending the move — nothing is mutated
        /// until <c>ExecuteMoveStage</c> commits positions. False for moves the player did not choose and
        /// may not decline (a rule-triggered move; see <c>GameOperationServices</c>), where a Cancelled
        /// reply has no meaning. Mirrors <see cref="SelectionRequest{T}"/>'s AllowCancel.
        /// </summary>
        public bool AllowCancel { get; }

        /// <summary>
        /// True only for the unit's MAIN move of its activation (built by <c>DefinePathStage</c>), where a
        /// Cancelled reply returns to the action menu. False for rule-triggered moves
        /// (<c>GameOperationServices</c>), where declining is final. The solo AI reads this to arm its
        /// decline latch (#358): a deterministic policy that declines a menu-reopening move must not be
        /// offered the same pick again, or it loops the menu forever - while declining an optional
        /// triggered move must stay a latch-free "no thanks".
        /// </summary>
        public bool MainActivationMove { get; }

        [JsonConstructor]
        public DefineMovementPathRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null, bool canMoveThroughEnemies = false,
            bool ignoresDifficultTerrain = false, bool ignoresImpassibleTerrain = false,
            IReadOnlyList<WeaponRangeOverride>? weaponRangeOverrides = null,
            IReadOnlyList<ModelMoveBudgetInfo>? modelMoveBudgets = null,
            bool allowCancel = false, bool mainActivationMove = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
            IgnoresImpassibleTerrain = ignoresImpassibleTerrain;
            WeaponRangeOverrides = weaponRangeOverrides ?? new List<WeaponRangeOverride>();
            ModelMoveBudgets = modelMoveBudgets ?? new List<ModelMoveBudgetInfo>();
            AllowCancel = allowCancel;
            MainActivationMove = mainActivationMove;
        }

        public DefineMovementPathRequest(PlayerID targetPlayerID,  string taskName,
            DataBinding<UnitData> unitDataBinding, float maxAdvanceDistance, float maxRushDistance, float maxDistanceInches,
            IReadOnlyList<WeaponSightProfile>? weaponSightProfiles = null, bool canMoveThroughEnemies = false,
            bool ignoresDifficultTerrain = false, bool ignoresImpassibleTerrain = false,
            IReadOnlyList<WeaponRangeOverride>? weaponRangeOverrides = null,
            IReadOnlyList<ModelMoveBudgetInfo>? modelMoveBudgets = null,
            bool allowCancel = false, bool mainActivationMove = false)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            MaxAdvanceDistance = maxAdvanceDistance;
            MaxRushDistance = maxRushDistance;
            MaxDistanceInches = maxDistanceInches;
            WeaponSightProfiles = weaponSightProfiles ?? new List<WeaponSightProfile>();
            CanMoveThroughEnemies = canMoveThroughEnemies;
            IgnoresDifficultTerrain = ignoresDifficultTerrain;
            IgnoresImpassibleTerrain = ignoresImpassibleTerrain;
            WeaponRangeOverrides = weaponRangeOverrides ?? new List<WeaponRangeOverride>();
            ModelMoveBudgets = modelMoveBudgets ?? new List<ModelMoveBudgetInfo>();
            AllowCancel = allowCancel;
            MainActivationMove = mainActivationMove;
        }

        /// <summary>
        /// This model's (Advance, Rush, hard-cap) budget: its <see cref="ModelMoveBudgets"/> entry, or the
        /// unit-wide scalars when it has none. The single lookup every resolver uses so their move-preview
        /// caps agree with the authoritative per-model stage check.
        /// </summary>
        public (float advance, float rush, float maxDistance) BudgetFor(ModelID modelId)
        {
            foreach (ModelMoveBudgetInfo budget in ModelMoveBudgets)
            {
                if (budget.ModelId == modelId)
                {
                    return (budget.MaxAdvanceDistance, budget.MaxRushDistance, budget.MaxDistanceInches);
                }
            }
            return (MaxAdvanceDistance, MaxRushDistance, MaxDistanceInches);
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(CancellableResult<List<ModelMoveEntry>> resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    // Facings (#150), when present, is parallel to Positions: Facings[i] is the model's yaw facing (a unit
    // normal) at Positions[i]. The GUI movement resolver fills it — by default the direction of travel into
    // each waypoint, so a model turns through corners — so the executor can set facing per step (and future
    // presentation beats can lerp rotation along the path). Null → facing is left unchanged (AI moves, aircraft).
    public record ModelMoveEntry(DataBinding<ModelData> Model, List<Position> Positions, List<Float2>? Facings = null);

    /// <summary>
    /// One living model's per-model move budget (#093), carried on <see cref="DefineMovementPathRequest"/> so
    /// resolvers can preview/clamp each model against its own Advance/Rush/hard-cap. <see cref="MaxDistanceInches"/>
    /// is the collapsed hard cap (max of the model's Rush and its Melee-Shrouding-shrunk Charge), matching what
    /// the authoritative <c>DefinePathStage</c> re-validation enforces.
    /// </summary>
    public record ModelMoveBudgetInfo(ModelID ModelId, float MaxAdvanceDistance, float MaxRushDistance,
        float MaxDistanceInches);

    /// <summary>
    /// The per-model move caps a validator enforces: the Rush distance (the charge-reach gate) and the hard
    /// distance cap (Rush vs effective Charge, collapsed). Per-model so a joined hero's Fast/Slow gives that
    /// model its own budget while the rest of the unit keeps theirs (#093); a unit with no per-model movement
    /// rules has the same budget for every model — identical to the pre-#093 unit-wide scalars.
    /// </summary>
    public readonly record struct ModelMoveBudget(float MaxRushDistance, float MaxDistanceInches);

    /// <summary>
    /// One of the moving unit's ranged weapons and whether it ignores cover (Blast) / line of sight
    /// (Indirect, Takedown). <see cref="CoverIgnoreRule"/> / <see cref="LineOfSightIgnoreRule"/> carry the
    /// alias-aware display name of the responsible rule (null when none), so the movement overlay can
    /// attribute the effect on each fire-line label, e.g. "Huge Gun (Indirect ignores line of sight)".
    /// </summary>
    public record WeaponSightProfile(Weapon Weapon, bool IgnoresCover, bool IgnoresTerrain,
        string? CoverIgnoreRule = null, string? LineOfSightIgnoreRule = null);

    /// <summary>
    /// The effective shooting range (inches) of one of the mover's ranged weapons against one specific enemy
    /// unit, once range-modifier rules (#102) are folded in — Increased Shooting Range widens it, Ranged
    /// Shrouding narrows it. Keyed by weapon name + the enemy unit's <see cref="UnitID"/> so the movement
    /// targeting overlay can look it up; only emitted when it differs from the weapon's base range.
    /// </summary>
    public record WeaponRangeOverride(string WeaponName, UnitID EnemyUnitId, float EffectiveRangeInches);
}
