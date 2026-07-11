
// #204: where a group of successful hits came from, so the save-roll presentation can explain WHY there
// are extra wounds ("2 hits x3 (Blast) = 6", "3 hits +1 (Furious)", "2 hits, Rending AP+1"). Carried on
// each SuccessfulHitInfo and threaded to the save stage, which groups hits by save threshold and composes
// the arithmetic from the sources of each merged group. Purely presentational - never affects the math.
public enum EHitSourceKind
{
    // Hits that were actually rolled and save at the weapon's base AP.
    BaseRolled,
    // Extra hits an on-6 rule spawned (Furious / Surge / Relentless). Additive; same save threshold as base.
    ExtraHits,
    // Overflow hits a multiplier rule added (Blast). Same save threshold as base.
    BlastMultiplier,
    // Rolled hits a per-hit-AP rule (Rending / Crack) peeled into their own raised-threshold group.
    PerHitAp,
}

public readonly struct HitGroupSource
{
    public EHitSourceKind Kind { get; }
    // Alias-aware rule name that produced these hits (empty for BaseRolled).
    public string RuleName { get; }
    // BlastMultiplier: the multiplier (x3). PerHitAp: the AP delta (+1). 0/unused otherwise.
    public float Amount { get; }

    public HitGroupSource(EHitSourceKind kind, string ruleName = "", float amount = 0f)
    {
        Kind = kind;
        RuleName = ruleName ?? "";
        Amount = amount;
    }

    public static HitGroupSource Base => new HitGroupSource(EHitSourceKind.BaseRolled);
}

public struct SuccessfulHitInfo
{
    public float HitCount => Rolls.TotalRolls;

    public IDiceResults Rolls { get; }

    // #016 Per-hit-group save modifier: an effect carried on these specific hits that shifts the
    // defender's save threshold (same sign convention as RollToHitResults.SaveModifier — a negative
    // raises the threshold). DetermineSaveRollsNeededStage applies it per group, stacking with the
    // unit-wide carry. 0 when no per-hit effect tagged this group.
    public int SaveModifier { get; }

    // #204 Presentation provenance: where these hits came from, so the save stage can explain the volley.
    public HitGroupSource Source { get; }

    public SuccessfulHitInfo(IDiceResults diceResults, int saveModifier = 0)
    {
        Rolls = diceResults;
        SaveModifier = saveModifier;
        Source = HitGroupSource.Base;
    }

    public SuccessfulHitInfo(IDiceResults diceResults, int saveModifier, HitGroupSource source)
    {
        Rolls = diceResults;
        SaveModifier = saveModifier;
        Source = source;
    }
}
