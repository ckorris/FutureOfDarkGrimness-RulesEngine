
public struct SuccessfulSaveInfo
{
    public float SaveCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }

    public PendingSaveRolls RollNeededInfo { get; }

    public SuccessfulSaveInfo(IDiceResults diceResults, PendingSaveRolls rollNeededInfo)
    {
        Rolls = diceResults;
        RollNeededInfo = rollNeededInfo;
    }
}
