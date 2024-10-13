
public interface IDiceRoller
{
    public IDiceResults Roll(int sideCount, float rollCount);
}

public static class IDiceRollerExtensions
{
    public const int DEFAULT_SIDE_COUNT = 6;

    public static IDiceResults Roll(this IDiceRoller roller, float rollCount)
    {
        return roller.Roll(DEFAULT_SIDE_COUNT, rollCount);
    }
}
