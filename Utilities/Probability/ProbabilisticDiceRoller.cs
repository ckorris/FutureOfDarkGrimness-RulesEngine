using System;

public class ProbabilisticDiceRoller : IDiceRoller
{
    private static readonly Random _random = new Random();

    public IDiceResults Roll(int sideCount, float rollCount)
    {
        float[] rolls = new float[sideCount];
        float valueOfAllSides = rollCount / sideCount;

        for(int i = 0; i < rolls.Length; i++)
        {
            rolls[i] = valueOfAllSides;
        }

        return new DiceResults(rolls);
    }

    /// <summary>
    /// A decisive roll has a discrete consequence that cannot be averaged, so even in probabilistic
    /// mode it resolves to one concrete face via real randomness rather than the expected-value spread
    /// that <see cref="Roll"/> produces. Without this, a single morale die would read as "0.5 of a
    /// success" and every meaningful morale test would auto-fail.
    /// </summary>
    public IDiceResults RollDecisive(int sideCount)
    {
        float[] rolls = new float[sideCount];
        int face = _random.Next(1, sideCount + 1); // upper bound exclusive
        rolls[face - 1] = 1f;
        return new DiceResults(rolls);
    }
}
