

using System;

public static class DiceUtilities
{
    public const int MINIMUM_SUCCESS_ROLL = 2;

    public const int MAXIMUM_SUCCESS_ROLL = 6;

    /// <summary>
    /// Forces a number to be within the range of possible rolls on a D6 that can ever be successful. 
    /// Use this to keep the rule true that a 6 is always a success and a 1 is always a failure 
    /// after applying modifiers. 
    /// </summary>
    /// <param name="unclampedRoll"></param>
    /// <returns></returns>
    public static int ClampSuccessRollNeeded(int unclampedRoll)
    {
        return Math.Clamp(unclampedRoll, MINIMUM_SUCCESS_ROLL, MAXIMUM_SUCCESS_ROLL);
    }

}
