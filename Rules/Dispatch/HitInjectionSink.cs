using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class HitInjectionSink : IHitInjectionSink
{
    private float _extraHits;


    /// <summary>
    /// Write face (called by operations): adds <paramref name="count"/> hits to the total.
    /// </summary>
    public void AddHits(float count)
    {
        _extraHits += count;
    }

    /// <summary>
    /// Applies every hit-injection operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IHitInjectionSink> operation in operations.OfType<SinkOperation<IHitInjectionSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the hit-roll stage): the running total of extra hits to
    /// inject (0 if none). May be fractional under the probabilistic roller.
    /// </summary>
    public float TotalExtraHits => _extraHits;
}
