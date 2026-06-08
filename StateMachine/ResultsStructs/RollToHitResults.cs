using System.Collections.Generic;

namespace FDG
{

    public struct RollToHitResults
    {
        public List<SuccessfulHitInfo> SuccessfulHitList;

        public List<FailedHitInfo> FailedHitList;

        // #042: net save-roll modifier carried from hit-roll-complete rules (Rending) to the save
        // stage, which produces the op (where the unmodified hit roll is correct) but applies it on
        // the defender's save threshold. 0 when no such rule fired.
        public int SaveModifier;

        public RollToHitResults(List<SuccessfulHitInfo> successfulHits, List<FailedHitInfo> failedHitList)
        {
            SuccessfulHitList = successfulHits;
            FailedHitList = failedHitList;
            SaveModifier = 0;
        }
    }
}