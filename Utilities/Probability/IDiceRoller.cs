using System;

public interface IDiceRoller
{
    public IDiceResults Roll(int sideCount, float rollCount);

    /// <summary>
    /// Roll a single, fully realized die — exactly one face comes up. Unlike <see cref="Roll"/>, a
    /// decisive roll must never be spread across faces as an expected value: it has a discrete
    /// consequence (a morale pass/fail, a dangerous-terrain wound, an objective count) that cannot be
    /// meaningfully averaged. The default is a single <see cref="Roll"/>, which is already concrete for
    /// every realized roller; <see cref="ProbabilisticDiceRoller"/> overrides it so that it too yields a
    /// real outcome here instead of its usual per-face distribution.
    /// </summary>
    public IDiceResults RollDecisive(int sideCount) => Roll(sideCount, 1);
}

public static class IDiceRollerExtensions
{
    public const int DEFAULT_SIDE_COUNT = 6;

    public static IDiceResults Roll(this IDiceRoller roller, float rollCount)
    {
        return roller.Roll(DEFAULT_SIDE_COUNT, rollCount);
    }

    public static IDiceResults RollDecisive(this IDiceRoller roller)
    {
        return roller.RollDecisive(DEFAULT_SIDE_COUNT);
    }

    /// <summary>
    /// The single face a <see cref="IDiceRoller.RollDecisive"/> came up on, as a plain int. For code that
    /// needs to compare or rank raw rolls (roll-offs) rather than count successes against a threshold.
    /// Routing these through the roller — instead of a private <c>Random</c> — is what keeps them
    /// reproducible under a seed (#193); <c>RollDecisive</c> guarantees a concrete face even in
    /// probabilistic mode, which is why the old "we can't use the context roller here" workaround is gone.
    /// </summary>
    public static int RollDecisiveFace(this IDiceRoller roller, int sideCount = DEFAULT_SIDE_COUNT)
    {
        IDiceResults results = roller.RollDecisive(sideCount);

        for (int face = results.SideMin; face <= results.SideMax; face++)
        {
            if (results.At(face) >= 1f)
                return face;
        }

        throw new InvalidOperationException(
            $"A decisive d{sideCount} produced no concrete face; {roller.GetType().Name}.RollDecisive is broken.");
    }
}
