using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct SuccessfulHitInfo
{
    public float HitCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }


    public SuccessfulHitInfo(IDiceResults diceResults)
    {
        Rolls = diceResults;
    }
}