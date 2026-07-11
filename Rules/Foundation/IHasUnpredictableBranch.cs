namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface: a hit/save hook context that carries the <see cref="EUnpredictableBranch"/>
/// rolled for this attack action. Lets <see cref="Rules.Definitions.Condition.UnpredictableBranchIs"/>
/// gate the two halves of an Unpredictable rule - the +1-to-hit arm at
/// <see cref="EHookID.Shooting_OnHitRollModifier"/> and the AP/-1-save arm at
/// <see cref="EHookID.Shooting_OnHitRollComplete"/> - on the SAME decisive die. The die is rolled once
/// per attack action (see <see cref="Dispatch.UnpredictableBranchResolver"/>) and threaded down through
/// the combat metadata, so both hooks see one branch rather than rolling independently.
/// </summary>
public interface IHasUnpredictableBranch : ICapability
{
    EUnpredictableBranch UnpredictableBranch { get; }
}
