
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    public class DetermineMeleeWinnerStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION = "DetermineMeleeWinnerNeedsRoll";
        public const string DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION = "DetermineMeleeWinnerDoesntNeedRoll";

        public StageBinding OnNeedsRollToDecide;
        public StageBinding OnDoesntNeedRollToDecide;

        public DetermineMeleeWinnerStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnNeedsRollToDecide = new StageBinding(this);
            OnDoesntNeedRollToDecide = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            //Get wounds dealt by each side.
            float attackerWoundsDealt = context.DefenderRemainingWoundsAtStart - context.DefendingUnit.RemainingWounds();
            float defenderWoundsDealt = context.AttackerRemainingWoundsAtStart - context.AttackingUnit.RemainingWounds();

            // #021 Fear(X): a unit counts as dealing +X extra wounds for the who-won-melee check only (no
            // real wounds). Fire Melee_OnMeleeResolution for each side as the Actor and fold its own
            // ExtraMeleeWoundCount into its dealt total, so the loser — and thus who must test morale — can flip.
            MeleeResolutionContext resolution = new MeleeResolutionContext(attacker, defender);
            float attackerBonus = SumExtraMeleeWounds(resolution, attacker);
            float defenderBonus = SumExtraMeleeWounds(resolution, defender);
            if (attackerBonus > 0f)
                GameContext.Log($"{attacker.Name} counts as +{attackerBonus} wounds in melee (Fear).");
            if (defenderBonus > 0f)
                GameContext.Log($"{defender.Name} counts as +{defenderBonus} wounds in melee (Fear).");
            attackerWoundsDealt += attackerBonus;
            defenderWoundsDealt += defenderBonus;

            if (attackerWoundsDealt == defenderWoundsDealt)
            {
                GameContext.Log($"Attackers and defenders tied: {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
                await OnDoesntNeedRollToDecide.Activate(context);
            }
            else if (attackerWoundsDealt > defenderWoundsDealt)
            {
                GameContext.Log($"Attackers won melee {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
                await OnNeedsRollToDecide.Activate(context);
            }
            else
            {
                GameContext.Log($"Defenders won melee {defenderWoundsDealt} vs. {attackerWoundsDealt}.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
                await OnNeedsRollToDecide.Activate(context);
            }
        }

        // Sum the extra melee wounds (Fear) the actor contributes to its own who-won total. The actor
        // sits in the Actor seat (Fear's default), so only its rules fire; the opponent's Fear is summed
        // by the symmetric call. ExtraMeleeWoundCount arrives already resolved to an int.
        private float SumExtraMeleeWounds(MeleeResolutionContext resolution, IUnit actor)
        {
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                resolution, RuleParticipant.Actor(actor));

            float extra = 0f;
            foreach (RuleOperation operation in operations)
            {
                if (operation is RuleOperation.ExtraMeleeWoundCount fear) extra += fear.Amount;
            }
            return extra;
        }
    }

    public readonly struct DetermineMeleeWinnerResults
    {
        public readonly EMeleeWinnerResult Winner;

        public DetermineMeleeWinnerResults(EMeleeWinnerResult result)
        {
            Winner = result;
        }

        public enum EMeleeWinnerResult
        {
            AttackerWon,
            DefenderWon,
            Tie
        }
    }
}
