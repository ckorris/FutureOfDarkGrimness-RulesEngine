
using System.Collections.Generic;

namespace FDG
{
    public struct RollToSaveResults
    {
        public readonly List<SuccessfulSaveInfo> SuccessfulSaveList;
        public readonly List<FailedSaveInfo> FailedSaveList;

        public RollToSaveResults(List<SuccessfulSaveInfo> successfulSaveList, List<FailedSaveInfo> failedSaveList)
        {
            SuccessfulSaveList = successfulSaveList;
            FailedSaveList = failedSaveList;
        }
    }
}