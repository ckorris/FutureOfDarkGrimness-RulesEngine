using System.Collections;
using System.Collections.Generic;

public struct FailedHitInfo //TODO: Maybe just reuse the successful version with a neutral name?
{
    public float HitCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }

    public FailedHitInfo(IDiceResults diceResults)
    {
        Rolls = diceResults;
    }
}
