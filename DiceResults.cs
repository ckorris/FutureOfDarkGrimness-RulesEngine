


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

    

    private float TotalWithinRange(int lowestRoll, int highestRoll)
    {
        float total = 0;
        //for (int i = lowestRoll - 1; i < highestRoll; i++)
        for (int i = lowestRoll - SideMin; i < highestRoll; i++)
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
