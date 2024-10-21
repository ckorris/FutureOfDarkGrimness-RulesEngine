
namespace FDG.Stages
{
    public class DetermineMeleeWinnerStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION = "DetermineMeleeWinnerNeedsRoll";
        public const string DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION = "DetermineMeleeWinnerDoesntNeedRoll";

        public DetermineMeleeWinnerStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //Get wounds dealt by each side.
            float attackerWoundsDealt = Context.DefenderRemainingWoundsAtStart - Context.DefendingUnit.RemainingWounds;
            float defenderWoundsDealt = Context.AttackerRemainingWoundsAtStart - Context.AttackingUnit.RemainingWounds;

            //TODO: This needs to have effects somehow so things like fear or fearless can apply.
            //Right now, it's a problem that this isn't a combat stage.

            if(attackerWoundsDealt == defenderWoundsDealt)
            {
                Context.Log($"Attackers and defenders tied: {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                Context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
                MoveToNextWithoutMoraleRoll();
            }
            else if (attackerWoundsDealt > defenderWoundsDealt)
            {
                Context.Log($"Attackers won melee {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                Context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
                MoveToRollMorale();
            }
            else
            {
                Context.Log($"Defenders won melee {defenderWoundsDealt} vs. {attackerWoundsDealt}.");
                Context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
                MoveToRollMorale();
            }

        }

        private void MoveToRollMorale()
        {
            SignalEvent(DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION);
        }

        private void MoveToNextWithoutMoraleRoll()
        {
            SignalEvent(DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION);
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
