using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds wound-ignore operations (Regeneration's "ignore each wound on a roll of X+") into a
/// single effective threshold. Multiple ignore sources don't stack into multiple rolls in the
/// corpus, so the sink keeps the BEST (lowest) threshold — the easiest ignore — and the stage
/// rolls one die per wound at that value. (Per-source separate rolls are a Phase-8 nuance the day
/// a second ignore rule exists.) The roll itself lives in the stage, not here: rolling is
/// execution, and keeping the sink a pure fold matches the other four sinks.
/// </summary>
public sealed class WoundIgnoreSink : IWoundIgnoreSink
{
    private int? _bestThreshold;

    /// <summary>
    /// Write face (called by operations): records an "ignore on <paramref name="minRoll"/>+",
    /// keeping the lowest threshold seen.
    /// </summary>
    public void IgnoreOn(int minRoll)
    {
        if (_bestThreshold is null || minRoll < _bestThreshold)
        {
            _bestThreshold = minRoll;
        }
    }

    /// <summary>
    /// Applies every wound-ignore operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IWoundIgnoreSink> operation in operations.OfType<SinkOperation<IWoundIgnoreSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: whether any ignore rule fired (and survived suppression). </summary>
    public bool HasIgnore => _bestThreshold.HasValue;

    /// <summary>
    /// Read face: the roll value at or above which a wound is ignored. Only meaningful when
    /// <see cref="HasIgnore"/> is true.
    /// </summary>
    public int Threshold => _bestThreshold ?? 0;
}
