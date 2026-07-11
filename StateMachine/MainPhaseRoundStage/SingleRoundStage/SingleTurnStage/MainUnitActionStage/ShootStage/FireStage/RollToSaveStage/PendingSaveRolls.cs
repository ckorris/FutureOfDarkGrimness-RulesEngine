
public struct PendingSaveRolls
{
    public IDiceResults HitRolls { get; }
    public float HitCount => HitRolls.TotalRolls;
    public int SaveNeeded { get; }

    // #204: where these hits came from, so the save stage can group by threshold and explain the volley.
    public HitGroupSource Source { get; }

    public PendingSaveRolls(IDiceResults hitRolls, int saveNeeded)
        : this(hitRolls, saveNeeded, HitGroupSource.Base)
    {
    }

    public PendingSaveRolls(IDiceResults hitRolls, int saveNeeded, HitGroupSource source)
    {
        HitRolls = hitRolls;
        SaveNeeded = saveNeeded;
        Source = source;
    }
}
