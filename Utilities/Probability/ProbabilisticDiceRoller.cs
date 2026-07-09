using System;
using FDG;

public class ProbabilisticDiceRoller : IDiceRoller
{
    // #193: per-instance, never static. A static Random would be shared by every game in the process,
    // so concurrent self-play games would consume each other's numbers (nondeterministic, and a race).
    private readonly Random _random;

    /// <param name="seed">
    /// Seeds the decisive-roll stream. Null keeps the historical unseeded behavior. <see cref="Roll"/>
    /// is pure expected-value math and never touches randomness, so the seed only governs
    /// <see cref="RollDecisive"/>.
    /// </param>
    public ProbabilisticDiceRoller(int? seed = null)
    {
        _random = GameRandom.Create(seed, GameRandom.SALT_GAME_CONTEXT);
    }

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
