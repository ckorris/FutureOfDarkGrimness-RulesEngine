using System.Collections.Generic;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #197 P10 — the auto-wound counterpart to <see cref="SyntheticHitResolution"/>. Rolls a pool of dice
    /// and turns each success (>= a threshold) into one DIRECT wound that takes NO armor save but still
    /// faces the defender's wound-ignore rolls (Regeneration) and Tough allocation. Covers Ravage and
    /// Crossing Attack ("roll X dice; for each 6+ the target takes one wound").
    ///
    /// <para>Where <see cref="SyntheticHitResolution"/> feeds hits into <c>DetermineSaveRollsNeededStage ->
    /// RollToSaveStage -> AssignWoundsStage</c>, this skips the first two stages entirely: the owner ruled
    /// (2026-07-22) that these wounds allow no save. So the pool successes are handed straight to
    /// <see cref="AssignWoundsStage{TMetadata}"/> as pre-failed saves via <see cref="AsUnsavedWounds"/>,
    /// which is where Regeneration and Tough already live.</para>
    ///
    /// <para>The count is kept as the success sub-histogram's fractional <c>TotalRolls</c>, never rounded —
    /// under the probabilistic roller a pool of N d6 at 6+ yields N/6 wounds, and int-locking it would
    /// diverge realistic and probabilistic modes (the #100 dice invariant). This mirrors Impact's existing
    /// <c>AtOrAbove</c> discipline.</para>
    /// </summary>
    public static class SyntheticWoundResolution
    {
        /// <summary>
        /// Rolls <paramref name="diceCount"/> dice and returns the success sub-histogram (each face
        /// &gt;= <paramref name="successThreshold"/>), whose <c>TotalRolls</c> is the fractional wound count.
        /// Presents the pool roll as an offense beat. The face values are real, so a Rending-style per-hit
        /// reader could partition them later — but auto-wounds carry no such rules today.
        /// </summary>
        public static async Task<IDiceResults> RollWoundPool(IGameContext gameContext, int diceCount,
            int successThreshold, string label, string context)
        {
            IDiceResults pool = gameContext.DiceRoller.Roll(diceCount);
            IDiceResults wounds = pool.SubsetAtOrAbove(successThreshold);

            await gameContext.Presenter.Present(DiceRolledBeat.From(pool, successThreshold,
                gameContext.Settings.RandomnessType, label, RollTags.Count(wounds.TotalRolls, "wound"),
                category: ERollBeatCategory.Offense, context: context));

            return wounds;
        }

        /// <summary>
        /// Wraps a wound histogram as a <see cref="RollToSaveResults"/> whose FAILURES are every wound and
        /// whose successes are empty — so <see cref="AssignWoundsStage{TMetadata}"/> counts them all as
        /// wounds (its wound total sums the failed-save list) while its Regeneration / Tough handling still
        /// runs. No save roll ever happens: these wounds are unsaveable by ruling. The synthesized
        /// <see cref="PendingSaveRolls"/> threshold is unused (the reroll path reads only the successful
        /// list, which is empty), so 0 is a safe placeholder.
        /// </summary>
        public static RollToSaveResults AsUnsavedWounds(IDiceResults woundDice)
        {
            PendingSaveRolls pending = new PendingSaveRolls(woundDice, saveNeeded: 0);
            List<FailedSaveInfo> failed = new List<FailedSaveInfo> { new FailedSaveInfo(woundDice, pending) };
            return new RollToSaveResults(new List<SuccessfulSaveInfo>(), failed);
        }
    }
}
