using System.Collections.Generic;

namespace FDG
{

    public struct RollToHitResults
    {
        public List<SuccessfulHitInfo> SuccessfulHitList;

        public List<FailedHitInfo> FailedHitList;

        public RollToHitResults(List<SuccessfulHitInfo> successfulHits, List<FailedHitInfo> failedHitList)
        {
            SuccessfulHitList = successfulHits;
            FailedHitList = failedHitList;
        }
    }
}