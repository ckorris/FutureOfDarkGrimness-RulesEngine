namespace FDG.StageResolution
{
    /// <summary>
    /// Reply type for stage requests where the player can cancel/back out.
    /// Stages match on the concrete subtype:
    ///   <c>Selected&lt;T&gt;</c>  — player made a choice; .Value carries it.
    ///   <c>Cancelled&lt;T&gt;</c> — player wants to back out of this decision.
    /// Non-cancellable requests should keep returning their plain reply type
    /// rather than wrapping it.
    /// </summary>
    public abstract record CancellableResult<T>;

    public sealed record Selected<T>(T Value) : CancellableResult<T>;

    public sealed record Cancelled<T> : CancellableResult<T>;
}
