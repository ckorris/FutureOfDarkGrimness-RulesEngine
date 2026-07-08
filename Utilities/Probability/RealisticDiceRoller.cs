using System;

/// <summary>
/// Returns rolls where each value is an integer based on randomness, 
/// as real (and ideal) dice are. 
/// </summary>
public class RealisticDiceRoller : IDiceRoller
{
    // #167: when a seed is supplied the roller holds ONE Random so the whole game's roll sequence is
    // reproducible. Unseeded keeps the historical behavior (a fresh time-seeded Random per Roll call).
    private readonly Random? _seededRandom;

    public RealisticDiceRoller(int? seed = null)
    {
        _seededRandom = seed.HasValue ? new Random(seed.Value) : null;
    }

    public IDiceResults Roll(int sideCount, float rollCount)
    {
        int rollCountInt = (int)Math.Round(rollCount);

        float[] rolls = new float[sideCount];

        Random random = _seededRandom ?? new Random();

        for(int i = 0; i < rollCountInt; i++)
        {
            int roll = random.Next(1, sideCount + 1); //+1 to sideCount as the upper bound is not inclusive.
            rolls[roll - 1]++;
        }

        return new DiceResults(rolls);
    }
}
