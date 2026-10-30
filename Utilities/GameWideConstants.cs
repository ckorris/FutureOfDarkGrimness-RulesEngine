
namespace FDG
{
    public static class GameWideConstants
    {
        public const float MOVE_SHOOT_DISTANCE_INCHES = 6;

        public const float RUSH_DISTANCE_INCHES = 12;

        public const float CHARGE_DISTANCE_INCHES = 12;

        /// <summary>
        /// How close models must be to engage in melee, after the charge has already happened.
        /// This is for horizontal (base-to-base) distances. Core Rules v3.6.0: "Models within 3" of
        /// enemies must strike with all melee weapons" (#417; 2" in v3.5.1). The one strike-range
        /// figure: charge availability, defender eligibility, the strike check, the charge-reach
        /// validator, the movement overlay's charge hints and the AI's melee threat reach all read it.
        /// </summary>
        public const float MELEE_RANGE_INCHES_HORIZONTAL = 3;

        /// <summary>
        /// How close models must be to engage in melee, after the charge has already happened.
        /// This is for vertical distance. OPR gives no vertical figure; 4" is this engine's convention
        /// (#022), kept unchanged when the horizontal range went to 3" (#417, owner's answer to plan Q4).
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
        /// The standoff (base-to-base) from enemy models. Core Rules v3.6.0: "Units may only be within 1"
        /// of other units when charging". A non-charge move may still END inside it (#206 - the movement
        /// validator does not reject it, and never lets a base overlap or pass through an enemy); the
        /// consequence is the forced charge: such a unit cannot Pass, nor Shoot while a charge is on
        /// offer - it must Charge (<c>ForcedChargeUtilities</c>, #334/#390). The band from here out to
        /// <see cref="MELEE_RANGE_INCHES_HORIZONTAL"/> (1"-3" since #417) may Charge but is not forced.
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
