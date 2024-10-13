
public struct PendingSaveRolls 
{
    public IDiceResults HitRolls { get; }
    public float HitCount => HitRolls.TotalRolls;
    public int SaveNeeded { get; }

    public PendingSaveRolls(IDiceResults hitRolls, int saveNeeded)
    {
        HitRolls = hitRolls;
        SaveNeeded = saveNeeded;
    }
}
