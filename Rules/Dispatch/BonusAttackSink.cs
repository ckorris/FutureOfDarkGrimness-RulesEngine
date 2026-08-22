using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds bonus-attack operations (#376 Bloodthirsty Fighter) into one total, by SUM - each blocked
/// die's unmodified 1 earns its own follow-up attack, so sources accumulate like the other injection
/// sinks (HitInjection/WoundInjection), not like the take-best value sinks. Kept fractional: under
/// the probabilistic roller the count is a histogram-derived expectation (the #100 dice invariant).
/// </summary>
public sealed class BonusAttackSink : IBonusAttackSink
{
    public float TotalBonusAttacks { get; private set; }

    public void AddBonusAttacks(float count)
    {
        if (count > 0f)
        {
            TotalBonusAttacks += count;
        }
    }

    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IBonusAttackSink> operation
                 in operations.OfType<SinkOperation<IBonusAttackSink>>())
        {
            operation.ApplyTo(this);
        }
    }
}
