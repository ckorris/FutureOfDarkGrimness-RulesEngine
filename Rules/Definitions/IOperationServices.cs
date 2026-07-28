namespace FDG.Rules.Definitions;

/// <summary>
/// The engine-services face an <see cref="ExecutableOperation"/> calls to enact an imperative
/// rule effect against live game state — the imperative-op analog of a sink's write interface.
/// It lets <see cref="RuleOperation"/> records in the Definitions layer drive engine subsystems
/// (movement, and later reactivation / deal-hits) without the Definitions layer depending on the
/// engine assembly; the concrete implementation lives engine-side.
/// <para>
/// Members are added one at a time as imperative operations are wired. <see cref="MoveUnit"/>
/// (<see cref="RuleOperation.InvokeTriggeredMove"/>) and <see cref="ApplyFatigue"/>
/// (<see cref="RuleOperation.InvokeApplyFatigue"/>) exist so far; reactivation and deal-hits add
/// their members when those slices land.
/// </para>
/// </summary>
public interface IOperationServices
{
    /// <summary>
    /// Move <paramref name="unit"/> up to <paramref name="maxInches"/>, acquiring the destination
    /// from <paramref name="controller"/> (the player who directs the move) — defaulting to the
    /// unit's own owner when null, which is every self-move (Vanguard/Harassing). A cross-unit
    /// forced move (e.g. a "reposition an enemy unit" spell) passes the rule's bearer instead, so
    /// the caster directs the displacement rather than the victim. When <paramref name="isOptional"/>
    /// the player may decline by submitting a no-op move. Resolution of
    /// <see cref="RuleOperation.InvokeTriggeredMove"/>.
    /// </summary>
    Task MoveUnit(IUnit unit, float maxInches, bool isOptional, PlayerID? controller = null);

    /// <summary>
    /// Fatigue <paramref name="unit"/> for the rest of the round (the #020 melee penalty). Resolution of
    /// <see cref="RuleOperation.InvokeApplyFatigue"/>; delegates to the engine's fatigue authority so the
    /// round-end clear and idempotency live in one place.
    /// </summary>
    Task ApplyFatigue(IUnit unit);

    /// <summary>
    /// Roll one die for <paramref name="unit"/>; on <paramref name="minRoll"/>+ remove every
    /// <paramref name="tokenType"/> token it holds. Resolution of
    /// <see cref="RuleOperation.InvokeClearTokenOnRoll"/> (round-start Shaken recovery). The implementation
    /// must roll DECISIVELY — the outcome is binary, so a probabilistic roller has to yield a concrete face
    /// rather than a fractional one.
    /// </summary>
    Task ClearTokenOnRoll(IUnit unit, Rules.Foundation.TokenType tokenType, int minRoll);

    /// <summary>
    /// Roll one die; on <paramref name="minRoll"/>+ add <paramref name="token"/> to
    /// <paramref name="unit"/>'s container. Resolution of
    /// <see cref="RuleOperation.InvokeGrantTokenOnRoll"/> (the Spotter family's "on a 4+ place a
    /// marker", #100 #14b). Same decisive-roll requirement as <see cref="ClearTokenOnRoll"/>.
    /// </summary>
    Task GrantTokenOnRoll(IUnit unit, Rules.Tokens.Token token, int minRoll);

    /// <summary>
    /// Remove <paramref name="unit"/> from the table for a mandatory Ambush return next round
    /// (#197 P22 Ambush Re-Deployment): drop any objective its side holds within 1" of its living
    /// models, park the models off-table in reserve, and stamp
    /// <see cref="Rules.Foundation.TokenType.PendingAmbushArrival"/> so the rule's token-gated defer
    /// entry carries the return leg. Resolution of <see cref="RuleOperation.InvokeAmbushRedeploy"/>.
    /// </summary>
    Task RedeployAsAmbush(IUnit unit);

    /// <summary>
    /// Build a new unit from <paramref name="placer"/>'s army's auxiliary spec named
    /// <paramref name="specName"/> (#197 P17 Spawn/Split), register it with the army, let the owner
    /// place it fully within <paramref name="radiusInches"/> of <paramref name="placer"/>, and mark
    /// it to join the round in progress (owner-ruled 2026-07-28: a mid-round creation may activate
    /// this round). A name matching no spec warns and does nothing — the rule fired, but the army
    /// carries nothing to place.
    /// </summary>
    Task SpawnUnit(IUnit placer, string specName, float radiusInches);
}
