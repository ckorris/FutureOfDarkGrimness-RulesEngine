namespace FDG.Rules.Definitions;

/// <summary>
/// A <see cref="RuleOperation"/> that enacts an imperative effect by calling into engine
/// subsystems through <see cref="IOperationServices"/> — the imperative mirror of
/// <see cref="SinkOperation{TSink}"/> (which folds a scalar into a sink). An applicator runs a
/// queue's imperative ops with <c>OperationExecutor</c>: pull them with one
/// <c>OfType&lt;ExecutableOperation&gt;()</c> filter, then call <see cref="Execute"/> in queue order.
/// <para>
/// Operations that fold into a sink stay <see cref="SinkOperation{TSink}"/>; operations the engine
/// never enacts (e.g. <see cref="RuleOperation.SuppressRule"/>, resolved in the suppression
/// first-pass) stay plain <see cref="RuleOperation"/> and are matched by neither.
/// </para>
/// </summary>
public abstract record ExecutableOperation : RuleOperation
{
    public abstract Task Execute(IOperationServices services);
}
