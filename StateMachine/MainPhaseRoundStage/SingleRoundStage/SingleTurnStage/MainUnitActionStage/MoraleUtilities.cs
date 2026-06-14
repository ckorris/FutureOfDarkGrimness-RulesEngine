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
        /// Roll a single morale die against <paramref name="rollNeeded"/> (the unit's Quality). Returns
        /// true if the test is passed. Mirrors <see cref="RollForMoraleStage"/>: with the probabilistic
        /// roller, <c>AtOrAbove</c> is a probability, so only a certain pass counts as a pass.
        /// </summary>
        public static bool TakeMoraleTest(IGameContext gameContext, int rollNeeded)
        {
            IDiceResults roll = gameContext.DiceRoller.Roll(1);
            return roll.AtOrAbove(rollNeeded) >= 1f;
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
