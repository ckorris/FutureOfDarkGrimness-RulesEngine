
namespace FDG
{
    public static class GameWideConstants
    {
        public const float MOVE_SHOOT_DISTANCE_INCHES = 6;

        public const float RUSH_DISTANCE_INCHES = 12;

        public const float CHARGE_DISTANCE_INCHES = 12;

        /// <summary>
        /// How close models must be to engage in melee, after the charge has already happened.
        /// This is for horizontal distances.
        /// </summary>
        public const float MELEE_RANGE_INCHES_HORIZONTAL = 2;

        /// <summary>
        /// How close models must be to engage in melee, after the charge has already happened.
        /// This is for vertical distance.
        /// </summary>
        public const float MELEE_RANGE_INCHES_VERTICAL = 4;

        /// <summary>
        /// Furthest any model in a unit can be from the closest other model in the same unit.
        /// </summary>
        public const float MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES = 1;

        /// <summary>
        /// Furthest any model in a unit can be from any other model in the same unit.
        /// </summary>
        public const float MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES = 9;

        public const float DIFFICULT_TERRAIN_MOVE_CAP_INCHES = 6f;

        /// <summary>
        /// Models that aren't charging must keep at least this far (base-to-base) from enemy models.
        /// A move may end either at or beyond this standoff, or in base contact (a charge) — but not in
        /// the band between, and never overlapping or passing through an enemy base.
        /// </summary>
        public const float ENEMY_STANDOFF_DISTANCE_INCHES = 1f;

        public const float DEPLOYMENT_DISTANCE_INCHES = 9;

        public const float DEFAULT_TABLE_WIDTH_INCHES = 72;

        public const float DEFAULT_TABLE_HEIGHT_INCHES = 48;

        /// <summary>
        /// The number of rounds a game lasts before victory is calculated (the official GDF game length).
        /// After this many rounds have been reconciled the main phase hands off to victory calculation.
        /// </summary>
        public const int NUMBER_OF_ROUNDS = 4;

        /// <summary>
        /// Maximum number of distinct enemy units a single unit may target during one shoot action.
        /// </summary>
        public const int MAX_TARGETED_UNITS_PER_SHOOT_ACTION = 2;

        /// <summary>
        /// A Caster(X) can never hold more than this many spell tokens at once. Caster grants X tokens
        /// at the start of each round and unspent tokens carry over, so the round-start grant clamps the
        /// running total to this cap (#033).
        /// </summary>
        public const int MAX_SPELL_TOKENS = 6;

        /// <summary>
        /// Range within which other Caster units may spend their own spell tokens to modify a cast roll —
        /// friendly Casters for +1 each, enemy Casters for -1 each (#103). Measured unit-to-unit,
        /// base-to-base.
        /// </summary>
        public const float CASTER_ASSIST_RANGE_INCHES = 18f;
    }
}
