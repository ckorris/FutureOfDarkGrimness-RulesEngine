using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct FailedSaveInfo
{
    public float SaveCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }

    public PendingSaveRolls RollNeededInfo { get; }
    public FailedSaveInfo(IDiceResults diceResults, PendingSaveRolls rollNeededInfo)
    {
        Rolls = diceResults;
        RollNeededInfo = rollNeededInfo;
    }
}
