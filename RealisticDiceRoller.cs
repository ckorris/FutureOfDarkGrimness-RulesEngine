using System;

/// <summary>
/// Returns rolls where each value is an integer based on randomness, 
/// as real (and ideal) dice are. 
/// </summary>
public class RealisticDiceRoller : IDiceRoller
{
    public IDiceResults Roll(int sideCount, float rollCount)
    {
        int rollCountInt = UnityEngine.Mathf.RoundToInt(rollCount); //Kinda lazy but avoids BS.

        float[] rolls = new float[sideCount];

        Random random = new Random();

        for(int i = 0; i < rollCountInt; i++)
        {
            int roll = random.Next(1, sideCount + 1); //+1 to sideCount as the upper bound is not inclusive.
            rolls[roll - 1]++;
        }

        return new DiceResults(rolls);
    }
}
