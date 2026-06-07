using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds quality-floor operations (Reliable) into a single best floor. A lower Quality number is a
/// better skill, so "treated as 2+" improves a worse quality to 2 — the sink keeps the BEST (lowest)
/// floor seen and the stage applies <c>min(baseQuality, floor)</c> before per-roll modifiers. Authored
/// quality value, not roll-derived, so it stays an int (mirrors <c>RollModifierSink</c>).
/// </summary>
public sealed class QualityFloorSink : IQualityFloorSink
{
    private int? _bestFloor;

    /// <summary>
    /// Write face (called by operations): records a "treated as <paramref name="quality"/>+" floor,
    /// keeping the lowest (best) value.
    /// </summary>
    public void FloorTo(int quality)
    {
        if (_bestFloor is null || quality < _bestFloor)
        {
            _bestFloor = quality;
        }
    }

    /// <summary>
    /// Applies every quality-floor operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IQualityFloorSink> operation in operations.OfType<SinkOperation<IQualityFloorSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: whether any floor rule fired. </summary>
    public bool HasFloor => _bestFloor.HasValue;

    /// <summary>
    /// Read face: the quality value the base should be floored to. Only meaningful when
    /// <see cref="HasFloor"/> is true.
    /// </summary>
    public int Quality => _bestFloor ?? 0;
}
