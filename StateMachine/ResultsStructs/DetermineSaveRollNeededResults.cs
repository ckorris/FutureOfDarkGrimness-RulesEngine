using System.Collections.Generic;

namespace FDG
{
    public struct DetermineSaveRollNeededResults
    {
        public List<PendingSaveRolls> PendingSaveRollsList;

        // #245: display-ready chips explaining the attack-wide save arithmetic ("Defense 4+", "AP 2",
        // "Cover +1", "Shielded +1"), composed where the threshold is computed so the save beats can
        // show them. Per-bucket differences (Rending's per-hit AP) stay narrated in the beat label.
        // Null when the threshold is just the unmodified defense.
        public List<string>? ThresholdTags;

        public DetermineSaveRollNeededResults(List<PendingSaveRolls> pendingSaveRollsList)
        {
            PendingSaveRollsList = pendingSaveRollsList;
            ThresholdTags = null;
        }
    }
}