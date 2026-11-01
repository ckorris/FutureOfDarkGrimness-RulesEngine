namespace FDG.Rules.Foundation;

/// <summary>
/// Capability interface (#433): a hook context that knows the roll a hit needed - the attacker's Quality after
/// every modifier, clamped to the die (2..6) exactly as <c>RollToHitStage</c> splits successes from failures.
/// Lets a rule ask whether a natural face was a SUCCESSFUL hit: OPR 3.6's "Enhanced X" family gives "extra attacks
/// on successful unmodified hit results of 5-6", and a 5 that missed (Quality 6+, a -1 to hit, a Fatigued melee
/// swing) must not trigger it. Read by <see cref="Rules.Definitions.Condition.UnmodifiedRollHits"/>.
/// <para>
/// 0 means "no threshold - every rolled die is a hit": the synthetic-hit pipeline (spells, abilities) rolls
/// dice that are hits by construction, and a read-only forecast with no dice has nothing to gate.
/// </para>
/// </summary>
public interface IHasHitRollNeeded : ICapability
{
    int HitRollNeeded { get; }
}
