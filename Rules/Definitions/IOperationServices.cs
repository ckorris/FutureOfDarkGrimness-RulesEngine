namespace FDG.Rules.Definitions;

/// <summary>
/// The engine-services face an <see cref="ExecutableOperation"/> calls to enact an imperative
/// rule effect against live game state — the imperative-op analog of a sink's write interface.
/// It lets <see cref="RuleOperation"/> records in the Definitions layer drive engine subsystems
/// (movement, and later reactivation / deal-hits) without the Definitions layer depending on the
/// engine assembly; the concrete implementation lives engine-side.
/// <para>
/// Members are added one at a time as imperative operations are wired. Only
/// <see cref="MoveUnit"/> exists so far (<see cref="RuleOperation.InvokeTriggeredMove"/>);
/// reactivation and deal-hits add their members when those slices land.
/// </para>
/// </summary>
public interface IOperationServices
{
    /// <summary>
    /// Move <paramref name="unit"/> up to <paramref name="maxInches"/>, acquiring the destination
    /// from the unit's controller. When <paramref name="isOptional"/> the player may decline by
    /// submitting a no-op move. Resolution of <see cref="RuleOperation.InvokeTriggeredMove"/>.
    /// </summary>
    Task MoveUnit(IUnit unit, float maxInches, bool isOptional);
}
