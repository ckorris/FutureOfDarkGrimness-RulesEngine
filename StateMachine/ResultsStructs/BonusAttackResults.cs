namespace FDG
{
    /// <summary>
    /// #376 Bloodthirsty Fighter: the follow-up attacks a swing earned from the defender's block-roll
    /// 1s. Posted onto the swing's metadata by <c>AssignWoundsStage</c> (fold of
    /// <c>RuleOperation.AddBonusAttacks</c>), read by <c>ResolveBonusMeleeAttacksStage</c>, which runs
    /// them as a real child batch with the same weapon. Never posted for a batch that is itself a
    /// bonus batch — the no-chaining rule. Fractional under the probabilistic roller.
    /// </summary>
    public readonly struct BonusAttackResults
    {
        public readonly float AttackCount;

        public BonusAttackResults(float attackCount)
        {
            AttackCount = attackCount;
        }
    }
}
