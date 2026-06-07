using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class WoundModifierSink : IWoundModifierSink
{
    private int _netMultiplier = 1;

    /// <summary>
    /// Write face (called by operations): compounds <paramref name="factor"/> into
    /// the running multiplier.
    /// </summary>
    public void Multiply(int factor)
    {
        _netMultiplier *= factor;
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
