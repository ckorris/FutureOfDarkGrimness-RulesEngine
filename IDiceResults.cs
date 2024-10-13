
public interface IDiceResults
{
    public float this[int index] { get; }

    public int SideMin { get; }

    public int SideMax { get; }

    public float TotalRolls { get; }

    public float At(int rollNumber);

    public float AtOrAbove(int rollNumber);

    public float Above(int rollNumber);

    public float BelowOrAt(int rollNumber);

    public float Below(int rollNumber);

    public float Range(int lowestRoll, int highestRoll);

    public IDiceResults SubsetAt(int rollNumber);

    public IDiceResults SubsetAtOrAbove(int rollNumber);

    public IDiceResults SubsetAbove(int rollNumber);

    public IDiceResults SubsetBelowOrAt(int rollNumber);

    public IDiceResults SubsetBelow(int rollNumber);

    public IDiceResults SubsetRange(int lowestRoll, int highestRoll);
}
