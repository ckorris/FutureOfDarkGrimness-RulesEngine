using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Shared morale resolution used by both the melee morale path (<see cref="AssignMeleeMoralePenaltyStage"/>)
    /// and the ranged half-strength path (<see cref="ResolveRangedMoraleStage"/>): the d6-vs-Quality test
    /// and the two failure outcomes — Shaken, or Rout when the unit is at half strength or less.
    /// </summary>
    public static class MoraleUtilities
    {
        /// <summary>
        /// Roll a single decisive morale die against <paramref name="rollNeeded"/> (the unit's Quality).
        /// Returns true if the test is passed. Mirrors <see cref="RollForMoraleStage"/>: uses
        /// <c>RollDecisive</c> so the test resolves to a real binary outcome under either roller (#090).
        /// </summary>
        public static bool TakeMoraleTest(IGameContext gameContext, int rollNeeded)
        {
            // Decisive single die — one concrete face even under the probabilistic roller (#090).
            IDiceResults roll = gameContext.DiceRoller.RollDecisive();
            return roll.AtOrAbove(rollNeeded) >= 1f;
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
        public static bool? ResolveWoundDrivenMorale(IGameContext gameContext, DataBinding<UnitData> unitBinding,
            float remainingWoundsBefore)
        {
            IUnit unit = unitBinding.GetValue();
            if (!unit.GetIsAlive()) return null;                              // wiped out outright — no test
            if (!CrossedIntoHalfStrength(remainingWoundsBefore, unit)) return null;

            if (TakeMoraleTest(gameContext, unit.Quality)) return true;

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
