
using System;

public struct DiceResults : IDiceResults
{
    private const int DEFAULT_MIN_SIDE = 1;

    public readonly int SideMin { get; }
    public readonly int SideMax { get; }
    public readonly float TotalRolls { get; }

    public float this[int index]
    {
        get
        {
            return At(index);
        }
    }

    private readonly float[] _perSideValues;

    public DiceResults(float[] perSideValues, int sideMin = DEFAULT_MIN_SIDE)
    {
        SideMin = sideMin;
        SideMax = perSideValues.Length - 1 + SideMin;
        _perSideValues = perSideValues;

        TotalRolls = 0;
        for(int i = 0; i < perSideValues.Length; i++)
        {
            TotalRolls += _perSideValues[i];
        }
    }

    public float At(int rollNumber)
    {
        AssertValidSide(rollNumber);
        return _perSideValues[rollNumber - SideMin];
    }

    public float AtOrAbove(int rollNumber)
    {
        AssertValidSide(rollNumber);
        return TotalWithinRange(rollNumber, SideMax);
    }

    public float Above(int rollNumber)
    {
        AssertValidSide(rollNumber);
        return TotalWithinRange(rollNumber + 1, SideMax);
    }

    public float BelowOrAt(int rollNumber)
    {
        AssertValidSide(rollNumber);
        return TotalWithinRange(SideMin, rollNumber);
    }

    public float Below(int rollNumber)
    {
        AssertValidSide(rollNumber);
        return TotalWithinRange(SideMin, rollNumber - 1);
    }

    public float Range(int lowestRoll, int highestRoll)
    {
        AssertValidSide(lowestRoll);
        AssertValidSide(highestRoll);

        return TotalWithinRange(lowestRoll, highestRoll);
    }

    

    // Both bounds are face values and BOTH need the SideMin offset to index _perSideValues. The upper
    // bound used to be used raw (`i < highestRoll`), which is accidentally right for a full die - where
    // SideMin is 1, so `i < highestRoll` == `i <= highestRoll - 1` - and wrong for every SUBSET, whose
    // SideMin is its lowest kept face. On a SubsetAtOrAbove(4) of a d6 the array holds 3 entries while the
    // loop ran to i < 6, so any range query over a subset either over-counted or threw IndexOutOfRange.
    // Surfaced by #197's reroll-threshold work, which is the first caller to ask AtOrAbove of a subset.
    private float TotalWithinRange(int lowestRoll, int highestRoll)
    {
        float total = 0;
        for (int i = lowestRoll - SideMin; i <= highestRoll - SideMin; i++)
        {
            total += _perSideValues[i];
        }

        return total;
    }

    public IDiceResults SubsetAt(int rollNumber)
    {
        AssertValidSide(rollNumber);

        return GetSubsetWithinRange(rollNumber, rollNumber);
    }

    public IDiceResults SubsetAtOrAbove(int rollNumber)
    {
        AssertValidSide(rollNumber);

        return GetSubsetWithinRange(rollNumber, SideMax);
    }

    public IDiceResults SubsetAbove(int rollNumber)
    {
        AssertValidSide(rollNumber);

        return GetSubsetWithinRange(rollNumber + 1, SideMax);
    }

    public IDiceResults SubsetBelowOrAt(int rollNumber)
    {
        AssertValidSide(rollNumber);

        return GetSubsetWithinRange(SideMin, rollNumber);
    }

    public IDiceResults SubsetBelow(int rollNumber)
    {
        AssertValidSide(rollNumber);

        return GetSubsetWithinRange(SideMin, rollNumber - 1);
    }

    public IDiceResults SubsetRange(int lowestRoll, int highestRoll)
    {
        AssertValidSide(lowestRoll);
        AssertValidSide(highestRoll);

        return GetSubsetWithinRange(lowestRoll, highestRoll);
    }

    private IDiceResults GetSubsetWithinRange(int lowestRoll, int highestRoll)
    {
        int length = highestRoll - lowestRoll + 1;

        if(length <= 0)
        {
            return new DiceResults(new float[0], lowestRoll);
        }

        float[] copyValues = new float[highestRoll - lowestRoll + 1];

        Array.Copy(_perSideValues, lowestRoll - SideMin, copyValues, 0, copyValues.Length);

        return new DiceResults(copyValues, lowestRoll);
    }

    private void AssertValidSide(int side)
    {
        if(side < SideMin || side > SideMax)
        {
            throw new InvalidSideException(SideMax, side);
        }    
    }

    private class InvalidSideException : Exception
    {
        public InvalidSideException(int maxSide, int providedSide)
            : base($"Requested side out of bounds. Must be between 1 and {maxSide}. Given: {providedSide}.") { }
    }
}

public static class DiceResultsExtensions
{
    /// <summary>
    /// A copy of <paramref name="source"/> over the SAME face range with the given faces zeroed. Unlike the
    /// <c>Subset*</c> queries, which narrow the range (so <c>At</c> of a dropped face throws), every face stays
    /// addressable and the dropped ones simply read zero. Faces outside the range are ignored. Shared by the
    /// per-hit AP split's base remainder and the #418 special-rule view of a fatigued melee roll.
    /// </summary>
    public static IDiceResults WithoutFaces(this IDiceResults source, ICollection<int> faces)
    {
        if (faces.Count == 0) return source;

        int length = source.SideMax - source.SideMin + 1;
        float[] values = new float[length];
        for (int face = source.SideMin; face <= source.SideMax; face++)
        {
            values[face - source.SideMin] = faces.Contains(face) ? 0f : source.At(face);
        }

        return new DiceResults(values, source.SideMin);
    }

    /// <summary>
    /// #423: a copy of <paramref name="source"/> over the SAME face range with <paramref name="count"/> rolls removed,
    /// highest faces first (each face gives up min(its count, what is left) before the next lower one). Fractional
    /// counts - the probabilistic roller's - remove fractional face shares the same way. Used by the Parry step to take
    /// ignored hits out of a hit group: which faces go is cosmetic there (the save stages read a hit group's TotalRolls,
    /// never its faces), and top-down is deterministic. A count at or above the total yields an all-zero copy.
    /// </summary>
    public static IDiceResults WithoutTopRolls(this IDiceResults source, float count)
    {
        int length = source.SideMax - source.SideMin + 1;
        float[] values = new float[length];
        float remaining = count;
        for (int face = source.SideMax; face >= source.SideMin; face--)
        {
            float at = source.At(face);
            float removed = remaining > 0f ? Math.Min(at, remaining) : 0f;
            values[face - source.SideMin] = Math.Max(0f, at - removed);
            remaining -= removed;
        }

        return new DiceResults(values, source.SideMin);
    }
}
