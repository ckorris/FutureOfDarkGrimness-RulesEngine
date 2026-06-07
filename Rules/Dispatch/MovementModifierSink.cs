using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class MovementModifierSink : IMovementModifierSink
{
    private readonly Dictionary<EActionType, float> _deltas = new();

    public void Add(EActionType action, float deltaInches)
    {
        _deltas.TryGetValue(action, out float current);
        _deltas[action] = current + deltaInches;
    }

    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IMovementModifierSink> operation in operations.OfType<SinkOperation<IMovementModifierSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    public float Net(EActionType action)
    {
        return _deltas.TryGetValue(action, out float delta) ? delta : 0f;
    }
}