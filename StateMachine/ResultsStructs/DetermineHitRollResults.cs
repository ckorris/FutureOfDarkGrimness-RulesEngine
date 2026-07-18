
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

        // #245: display-ready chips explaining HOW HitRollNeeded came to be ("Quality 4+",
        // "Stealth -1", "Fatigued: 6s only"), composed where the arithmetic happens so the to-hit
        // beat can show them. Null when the threshold is just the unmodified quality (no chips).
        public System.Collections.Generic.List<string>? ThresholdTags;

        public DetermineHitRollResults(int hitRollNeeded, float attackCount)
        {
            HitRollNeeded = hitRollNeeded;
            AttackCount = attackCount;
        }
    }
}
