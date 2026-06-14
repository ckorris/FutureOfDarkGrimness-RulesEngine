
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
            //Get wounds dealt by each side.
            float attackerWoundsDealt = context.DefenderRemainingWoundsAtStart - context.DefendingUnit.RemainingWounds();
            float defenderWoundsDealt = context.AttackerRemainingWoundsAtStart - context.AttackingUnit.RemainingWounds();

            //TODO: This needs to have effects somehow so things like fear or fearless can apply.
            //Right now, it's a problem that this isn't a combat stage.

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
