using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds defense-set operations (Armor) into a single value. A lower Defense number is a better
/// save, so if several SetDefense land the sink keeps the lowest (best) — a unit is as armored as
/// its best armor. Against the unit's own BASE stat it is a literal SET, not a floor: the authored
/// value replaces the base even where the base was better (no current corpus site does). Authored
/// stat value, not roll-derived, so it stays an int (mirrors <c>MaxWoundsSink</c>).
/// </summary>
public sealed class DefenseSetSink : IDefenseSetSink
{
    private int? _bestSet;

    /// <summary>
    /// Write face (called by operations): records a "counts as Defense <paramref name="defense"/>+"
    /// set, keeping the lowest (best) value when several land.
    /// </summary>
    public void SetTo(int defense)
    {
        if (_bestSet is null || defense < _bestSet)
        {
            _bestSet = defense;
        }
    }

    /// <summary>
    /// Applies every defense-set operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IDefenseSetSink> operation in operations.OfType<SinkOperation<IDefenseSetSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: whether any defense-set rule fired. </summary>
    public bool HasSet => _bestSet.HasValue;

    /// <summary>
    /// Read face: the Defense value the unit's stat should be set to. Only meaningful when
    /// <see cref="HasSet"/> is true.
    /// </summary>
    public int Defense => _bestSet ?? 0;
}
