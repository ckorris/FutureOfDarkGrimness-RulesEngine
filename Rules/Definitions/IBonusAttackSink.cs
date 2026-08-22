namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of the bonus-attack fold (#376 Bloodthirsty Fighter: "for each unmodified 1
/// enemies roll blocking hits from this model's weapons in melee, +1 attack with that weapon").
/// An operation granting follow-up attacks (<see cref="RuleOperation.AddBonusAttacks"/>) applies
/// itself here; the wound stage folds the SUM (fractional under the probabilistic roller - the
/// count derives from a dice histogram and is never int-locked) and posts it for the melee swing
/// chain's bonus batch. The batch itself is a real child attack chain, not a sink read - see
/// ResolveBonusMeleeAttacksStage.
/// </summary>
public interface IBonusAttackSink
{
    void AddBonusAttacks(float count);
}
