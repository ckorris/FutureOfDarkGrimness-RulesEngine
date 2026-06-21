
public struct SuccessfulHitInfo
{
    public float HitCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }

    // #016 Per-hit-group save modifier: an effect carried on these specific hits that shifts the
    // defender's save threshold (same sign convention as RollToHitResults.SaveModifier — a negative
    // raises the threshold). DetermineSaveRollsNeededStage applies it per group, stacking with the
    // unit-wide carry. 0 when no per-hit effect tagged this group.
    public int SaveModifier { get; }

    public SuccessfulHitInfo(IDiceResults diceResults, int saveModifier = 0)
    {
        Rolls = diceResults;
        SaveModifier = saveModifier;
    }
}