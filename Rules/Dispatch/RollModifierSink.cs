using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class RollModifierSink : IRollModifierSink
{
    private readonly Dictionary<ERollKind, int> _deltas = new();

    
    /// <summary>
    /// Write face (called by operations): adds <paramref name="delta"/> to <paramref name="roll"/>.
    /// </summary>
    public void Add(ERollKind roll, int delta)
    {
        _deltas.TryGetValue(roll, out int current);
        _deltas[roll] = current + delta;
    }

    /// <summary>
    /// Applies every roll-modifier operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IRollModifierSink> operation in operations.OfType<SinkOperation<IRollModifierSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the stage): net modifier accumulated for <paramref name="roll"/> (0 if none).
    /// </summary>
    public int Net(ERollKind roll)
    {
        return _deltas.TryGetValue(roll, out int delta) ? delta : 0;
    }
}