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

        // #197 Hazardous: wounds the ATTACKING unit owes for its own unmodified 1s, counted at
        // hit-roll-complete (where the unmodified histogram is in hand) and applied by ApplyWoundsStage
        // once the target's wounds have landed - owner-ruled 2026-07-29. Carried rather than applied on the
        // spot because a combat stage's continuation after onFinished() never runs: the transition into the
        // next stage is effectively a tail call, so anything written after it is dead code in play. (It
        // LOOKS live under a test layer whose ExecuteTransition returns immediately - which is exactly how
        // the first cut of this rule passed its tests and did nothing in a real game.) 0 when none fired.
        public float SelfWounds;

        // #431 Pierce: the attack ignores every positive modifier to the target's Defense rolls. The defender's
        // positive Save deltas are already left out of SaveModifier when this is set; it is carried so
        // DetermineSaveRollsNeededStage also drops cover's +1 and any positive granted Defense buff. False
        // when no such rule fired.
        public bool IgnoresPositiveDefenseModifiers;

        public RollToHitResults(List<SuccessfulHitInfo> successfulHits, List<FailedHitInfo> failedHitList)
        {
            SuccessfulHitList = successfulHits;
            FailedHitList = failedHitList;
            SaveModifier = 0;
            ArmorPenetrationReduction = 0;
            SelfWounds = 0f;
            IgnoresPositiveDefenseModifiers = false;
        }
    }
}