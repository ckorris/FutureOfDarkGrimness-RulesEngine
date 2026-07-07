using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class WoundModifierSink : IWoundModifierSink
{
    private int _netMultiplier = 1;

    /// <summary>
    /// Write face (called by operations): records <paramref name="factor"/>, keeping the highest seen.
    /// If several multiplier sources land on one attack, the best one wins — take-the-best like the
    /// other value sinks (MaxWounds/QualityFloor/WoundIgnore), not compounding.
    /// </summary>
    public void Multiply(int factor)
    {
        _netMultiplier = Math.Max(_netMultiplier, factor);
    }

    /// <summary>
    /// Applies every wound-modifier operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IWoundModifierSink> operation in operations.OfType<SinkOperation<IWoundModifierSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the wound stage): the net multiplier to apply to the
    /// attack's wound count (1 if no rule fired). Authored multiplier, not
    /// roll-derived, so it stays an int.
    /// </summary>
    public int NetMultiplier => _netMultiplier;
}
