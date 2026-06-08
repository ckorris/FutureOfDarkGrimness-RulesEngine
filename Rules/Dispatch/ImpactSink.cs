using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class ImpactSink : IImpactSink
{
    private int _totalDice;

    /// <summary>
    /// Write face (called by operations): adds <paramref name="count"/> impact dice to the total.
    /// </summary>
    public void AddDice(int count)
    {
        _totalDice += count;
    }

    /// <summary>
    /// Applies every charge-impact operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IImpactSink> operation in operations.OfType<SinkOperation<IImpactSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the charge-contact stage): the total number of impact dice to roll
    /// (0 if no Impact rule fired). Authored dice count, not roll-derived, so it stays an int.
    /// </summary>
    public int TotalDice => _totalDice;
}
