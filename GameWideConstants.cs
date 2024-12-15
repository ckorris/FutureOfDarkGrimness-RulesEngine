using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG
{
    public static class GameWideConstants
    {
        public static float MOVE_SHOOT_DISTANCE_INCHES = 6;
        
        public static float CHARGE_DISTANCE_INCHES = 12;

        /// <summary>
        /// Furthest any model in a unit can be from the closest other model in the same unit.
        /// </summary>
        public static float MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES = 1;

        /// <summary>
        /// Furthest any model in a unit can be from any other model in the same unit.
        /// </summary>
        public static float MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES = 9;
    }
}
