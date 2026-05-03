
namespace FDG.Utilities
{
    /// <summary>
    /// Ways to compare floats to another while allowing leeway for floating point precision.
    /// </summary>
    public static class FloatExtensions
    {
        //Much more forgiving than normal, because this is for a board game where in real life,
        //measurements would be off by a lot more than this. 
        private const float DEFAULT_EPSILON = 0.0001f; 

        public static bool AlmostEquals(this float a, float b, float epsilon = DEFAULT_EPSILON)
        {
            return Math.Abs(a - b) < epsilon;
        }


        public static bool GreaterThanOrAlmostEqual(this float a, float b, float epsilon = DEFAULT_EPSILON)
        {
            return a > b || a.AlmostEquals(b, epsilon);
        }


        public static bool LessThanOrAlmostEqual(this float a, float b, float epsilon = DEFAULT_EPSILON)
        {
            return a < b || a.AlmostEquals(b, epsilon);
        }
    }
}

