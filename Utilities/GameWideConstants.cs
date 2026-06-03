
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

        public const float DEPLOYMENT_DISTANCE_INCHES = 9;

        public const float DEFAULT_TABLE_WIDTH_INCHES = 72;

        public const float DEFAULT_TABLE_HEIGHT_INCHES = 48;

        /// <summary>
        /// Maximum number of distinct enemy units a single unit may target during one shoot action.
        /// </summary>
        public const int MAX_TARGETED_UNITS_PER_SHOOT_ACTION = 2;
    }
}
