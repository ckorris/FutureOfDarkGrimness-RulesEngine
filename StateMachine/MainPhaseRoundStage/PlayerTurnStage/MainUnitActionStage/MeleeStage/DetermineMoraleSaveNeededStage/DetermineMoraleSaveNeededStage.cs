
namespace FDG.Stages
{
    public class DetermineMoraleSaveNeededStage : StateBase<IMeleeContext>
    {
        public const string DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION = "DetermineMoraleSaveNeededFinished";

        public DetermineMoraleSaveNeededStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            if (Context.QueryForResult(out DetermineMeleeWinnerResults meleeWinnerResult) == false)
            {
                throw new ArgumentException($"{nameof(DetermineMoraleSaveNeededStage)} called when there was not object" +
                    $"of type {nameof(DetermineMeleeWinnerResults)} in the context metadata.");
            }

            switch (meleeWinnerResult.Winner)
            {
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon:
                    Context.AddResult(new DetermineMoraleSaveNeededResult(Context.DefendingUnit.Quality));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon:
                    Context.AddResult(new DetermineMoraleSaveNeededResult(Context.AttackingUnit.Quality));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie:
                    throw new InvalidOperationException($"Somehow reached the {nameof(DetermineMoraleSaveNeededStage)} stage " + 
                        "when melee results were a tie.");
                default:
                    throw new ArgumentOutOfRangeException();
            }

            MoveToNextStage();

        }

        private void MoveToNextStage()
        {
            SignalEvent(DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION);
        }
    }

    public readonly struct DetermineMoraleSaveNeededResult
    {
        public readonly int RollNeeded;

        public DetermineMoraleSaveNeededResult(int rollNeeded)
        {
            RollNeeded = rollNeeded;
        }
    }
}
