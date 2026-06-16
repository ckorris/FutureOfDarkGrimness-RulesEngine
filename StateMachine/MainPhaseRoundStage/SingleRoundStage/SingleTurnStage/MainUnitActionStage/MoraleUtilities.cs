using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Shared morale resolution used by every morale path — the melee loss (<see cref="RollForMoraleStage"/>
    /// → <see cref="AssignMeleeMoralePenaltyStage"/>) and the wound-driven half-strength path (shooting via
    /// <see cref="ResolveRangedMoraleStage"/>, dangerous terrain): the rule-aware morale test and the two
    /// failure outcomes — Shaken, or Rout when the unit is at half strength or less.
    /// </summary>
    public static class MoraleUtilities
    {
        // Fearless's second-chance die passes on a 4+ regardless of Quality (rulebook fixed threshold).
        private const int FEARLESS_REROLL_PASSES_ON = 4;

        /// <summary>The result of a morale test: whether it passed, the (post-modifier, clamped) roll it
        /// needed, and whether it only passed via a granted re-roll (Fearless) — the last two for logging.</summary>
        public readonly struct MoraleTestOutcome
        {
            public readonly bool Passed;
            public readonly int RollNeeded;
            public readonly bool PassedViaReroll;

            public MoraleTestOutcome(bool passed, int rollNeeded, bool passedViaReroll)
            {
                Passed = passed;
                RollNeeded = rollNeeded;
                PassedViaReroll = passedViaReroll;
            }
        }

        /// <summary>
        /// Take a morale test for <paramref name="testingUnit"/> against <paramref name="baseRollNeeded"/>
        /// (its Quality), applying morale special rules. Fires <see cref="EHookID.Morale_OnPreMoraleTest"/>
        /// and folds any <c>ApplyRollModifier(Morale)</c> into the threshold (a +N makes the test easier, so
        /// it lowers the roll needed; clamped so a 1 always fails and a 6 always passes). The test is a
        /// single decisive die (#090). On a failure it fires <see cref="EHookID.Morale_OnMoraleTestComplete"/>
        /// and, if a morale re-roll is granted (Fearless), rolls one more decisive die that passes on a 4+
        /// regardless of Quality.
        /// </summary>
        public static async Task<MoraleTestOutcome> TakeMoraleTest(IGameContext gameContext, IUnit testingUnit, int baseRollNeeded)
        {
            IReadOnlyList<RuleOperation> preTestOps = gameContext.RuleEvaluator.EvaluateAll(
                new PreMoraleTestContext(testingUnit), (testingUnit, ERuleSeat.Actor, null));
            RollModifierSink modifiers = new RollModifierSink();
            modifiers.ApplyFrom(preTestOps);
            int rollNeeded = DiceUtilities.ClampSuccessRollNeeded(baseRollNeeded - modifiers.Net(ERollKind.Morale));

            // Decisive single die — one concrete face even under the probabilistic roller (#090).
            IDiceResults initialRoll = gameContext.DiceRoller.RollDecisive();
            bool passedInitial = initialRoll.AtOrAbove(rollNeeded) >= 1f;
            await gameContext.Presenter.Present(DiceRolledBeat.From(initialRoll, rollNeeded,
                gameContext.Settings.RandomnessType, "Morale Test", passedInitial ? "Passed" : "Failed"));
            if (passedInitial)
            {
                return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: false);
            }

            // Failed — offer morale rules a second chance (Fearless: a fresh decisive die, 4+ passes).
            IReadOnlyList<RuleOperation> completeOps = gameContext.RuleEvaluator.EvaluateAll(
                new MoraleTestContext(testingUnit), (testingUnit, ERuleSeat.Actor, null));
            RerollSink rerollSink = new RerollSink();
            rerollSink.ApplyFrom(completeOps);
            if (rerollSink.RerollMoraleOnFailure)
            {
                IDiceResults reroll = gameContext.DiceRoller.RollDecisive();
                bool passedReroll = reroll.AtOrAbove(FEARLESS_REROLL_PASSES_ON) >= 1f;
                await gameContext.Presenter.Present(DiceRolledBeat.From(reroll, FEARLESS_REROLL_PASSES_ON,
                    gameContext.Settings.RandomnessType, "Fearless Re-roll", passedReroll ? "Passed" : "Failed"));
                if (passedReroll)
                {
                    return new MoraleTestOutcome(passed: true, rollNeeded, passedViaReroll: true);
                }
            }

            return new MoraleTestOutcome(passed: false, rollNeeded, passedViaReroll: false);
        }

        /// <summary>
        /// Whether a unit just crossed into half strength: it was above half before taking wounds
        /// (<paramref name="remainingWoundsBefore"/>) and is at half strength or less now. Used so a
        /// wound-driven morale test fires only on the blow that reduces the unit to half, not on every
        /// later hit while it is already there.
        /// </summary>
        public static bool CrossedIntoHalfStrength(float remainingWoundsBefore, IUnit unitAfter)
        {
            bool wasAboveHalf = remainingWoundsBefore * 2f > unitAfter.MaxWounds;
            return wasAboveHalf && unitAfter.GetIsAtHalfStrength();
        }

        /// <summary>
        /// Resolve a wound-driven morale test (shooting, dangerous terrain): if the unit was just
        /// reduced to half strength or less, it tests, and a failure Routs it (it is at half strength by
        /// construction). Returns null if no test was taken (still above half, or destroyed outright),
        /// true if passed, false if failed and Routed. Logging is left to the caller, which knows the
        /// wound source.
        /// </summary>
        public static async Task<bool?> ResolveWoundDrivenMorale(IGameContext gameContext, DataBinding<UnitData> unitBinding,
            float remainingWoundsBefore)
        {
            IUnit unit = unitBinding.GetValue();
            if (!unit.GetIsAlive()) return null;                              // wiped out outright — no test
            if (!CrossedIntoHalfStrength(remainingWoundsBefore, unit)) return null;

            if ((await TakeMoraleTest(gameContext, unit, unit.Quality)).Passed) return true;

            Rout(unitBinding);
            return false;
        }

        /// <summary>
        /// Apply a failed morale test's consequence: the unit is Routed if it is at half strength or
        /// less, otherwise it becomes Shaken.
        /// </summary>
        public static void ApplyFailedMoraleOutcome(DataBinding<UnitData> unitBinding)
        {
            if (unitBinding.GetValue().GetIsAtHalfStrength())
            {
                Rout(unitBinding);
            }
            else
            {
                ApplyShaken(unitBinding);
            }
        }

        /// <summary>
        /// Mark a unit Shaken. The token clears manually when the unit spends an activation idle to
        /// recover (#008); applied idempotently.
        /// </summary>
        public static void ApplyShaken(DataBinding<UnitData> unitBinding)
        {
            IUnit unit = unitBinding.GetValue();
            if (unit.Tokens.HasToken(TokenType.Shaken)) return;

            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
        }

        /// <summary>
        /// Remove a unit from play by dealing lethal wounds to all its living models. The engine has no
        /// whole-unit removal primitive; an all-models-dead unit is already filtered out of activation,
        /// turn order, and objective scoring everywhere via <c>GetIsAlive</c>.
        /// </summary>
        public static void Rout(DataBinding<UnitData> unitBinding)
        {
            foreach (IModel model in unitBinding.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;

                float remaining = model.TotalWounds - model.WoundsDealt;
                if (remaining > 0f) model.DealWounds(remaining);
            }
        }
    }
}
