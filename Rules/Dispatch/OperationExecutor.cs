using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Runs the imperative operations in a rule-operation queue against live game state — the
/// imperative-op counterpart to a sink's <c>ApplyFrom</c>. It picks out the
/// <see cref="ExecutableOperation"/>s with a single category-level <c>OfType</c> filter (the same
/// shape as <see cref="SinkOperation{TSink}"/> folding) and runs each via the engine-supplied
/// <see cref="IOperationServices"/>, in queue order. Non-executable operations (sink ops folded
/// elsewhere, <see cref="RuleOperation.SuppressRule"/>, …) are ignored.
/// </summary>
public static class OperationExecutor
{
    public static async Task Execute(IEnumerable<RuleOperation> operations, IOperationServices services)
    {
        foreach (ExecutableOperation operation in operations.OfType<ExecutableOperation>())
        {
            await operation.Execute(services);
        }
    }
}
