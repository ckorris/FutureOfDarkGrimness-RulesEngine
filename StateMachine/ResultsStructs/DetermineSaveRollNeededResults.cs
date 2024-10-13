using System.Collections.Generic;

namespace FDG
{
    public struct DetermineSaveRollNeededResults
    {
        public List<PendingSaveRolls> PendingSaveRollsList;

        public DetermineSaveRollNeededResults(List<PendingSaveRolls> pendingSaveRollsList)
        {
            PendingSaveRollsList = pendingSaveRollsList;
        }
    }
}