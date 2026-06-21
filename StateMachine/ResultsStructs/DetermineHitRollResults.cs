
namespace FDG
{
    public struct DetermineHitRollResults
    {
        // What each attack die must meet or beat to hit (pre-clamp).
        public int HitRollNeeded;

        // How many attack dice to roll. Computed in DetermineHitRollStage alongside the threshold so
        // attack-count modifiers fold here (accumulate-before-use), the mirror of HitRollNeeded's
        // modifiers; RollToHitStage consumes it as the dice count.
        public float AttackCount;

        public DetermineHitRollResults(int hitRollNeeded, float attackCount)
        {
            HitRollNeeded = hitRollNeeded;
            AttackCount = attackCount;
        }
    }
}
