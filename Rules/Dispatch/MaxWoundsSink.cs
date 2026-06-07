using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Folds max-wounds operations (Tough) into a single wound count. The corpus has one Tough per unit,
/// but if several SetMaxWounds land the sink keeps the highest (a model is as tough as its best
/// source). Authored value, not roll-derived, so it stays an int.
/// </summary>
public sealed class MaxWoundsSink : IMaxWoundsSink
{
    private int? _maxWounds;

    /// <summary>
    /// Write face (called by operations): records a max-wounds value, keeping the highest seen.
    /// </summary>
    public void SetMax(int wounds)
    {
        if (_maxWounds is null || wounds > _maxWounds)
        {
            _maxWounds = wounds;
        }
    }

    /// <summary>
    /// Applies every max-wounds operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IMaxWoundsSink> operation in operations.OfType<SinkOperation<IMaxWoundsSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Read face: whether any max-wounds rule fired. </summary>
    public bool HasMax => _maxWounds.HasValue;

    /// <summary>
    /// Read face: the max wounds to set on each model. Only meaningful when <see cref="HasMax"/> is true.
    /// </summary>
    public int MaxWounds => _maxWounds ?? 0;
}
