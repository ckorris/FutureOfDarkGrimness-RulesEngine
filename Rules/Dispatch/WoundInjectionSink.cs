using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Accumulates extra wounds injected by rules (Shred) for one attack. The wound-side mirror of
/// <see cref="HitInjectionSink"/>: <see cref="RuleOperation.InsertExtraWounds"/> operations fold in
/// via <see cref="ApplyFrom"/>, and AssignWoundsStage reads <see cref="TotalExtraWounds"/> back and
/// adds it to the wound count — the stage never inspects an operation's concrete type.
/// </summary>
public sealed class WoundInjectionSink : IWoundInjectionSink
{
    private float _extraWounds;

    /// <summary> Write face (called by operations): adds <paramref name="count"/> wounds to the total. </summary>
    public void AddWounds(float count)
    {
        _extraWounds += count;
    }

    /// <summary> Applies every wound-injection operation in <paramref name="operations"/> to this sink. </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IWoundInjectionSink> operation in operations.OfType<SinkOperation<IWoundInjectionSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the wound-assignment stage): the running total of extra wounds to add
    /// (0 if none). May be fractional under the probabilistic roller.
    /// </summary>
    public float TotalExtraWounds => _extraWounds;
}
