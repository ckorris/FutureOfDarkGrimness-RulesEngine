
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
}
