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

        // Total weapon-AP reduction from defender (Subject) rules (Fortified), summed at hit-roll-complete
        // and carried here; the save stage clamps the weapon's AP by this, floored at 0. 0 when none fired.
        public int ArmorPenetrationReduction;

        // #245: display-ready chips naming the rules behind SaveModifier / ArmorPenetrationReduction
        // ("Shielded +1", "Thrust -1", "Fortified AP-1"), composed at hit-roll-complete where the named
        // ops are in hand; DetermineSaveRollsNeededStage folds them into the save beat's chips. Null
        // when no such rule fired.
        public List<string>? SaveModifierTags;

        public RollToHitResults(List<SuccessfulHitInfo> successfulHits, List<FailedHitInfo> failedHitList)
        {
            SuccessfulHitList = successfulHits;
            FailedHitList = failedHitList;
            SaveModifier = 0;
            ArmorPenetrationReduction = 0;
        }
    }
}