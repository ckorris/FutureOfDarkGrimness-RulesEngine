using FDG.Utilities;
using System;

namespace FDG.Stages
{
    public class DetermineMoraleSaveNeededStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_MORALE_SAVE_NEEDED_FINISHED_TRANSITION = "DetermineMoraleSaveNeededFinished";

        public StageBinding ToRollForMorale;
        public DetermineMoraleSaveNeededStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToRollForMorale = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.QueryForResult(out DetermineMeleeWinnerResults meleeWinnerResult) == false)
            {
                throw new ArgumentException($"{nameof(DetermineMoraleSaveNeededStage)} called when there was not object" +
                    $"of type {nameof(DetermineMeleeWinnerResults)} in the context metadata.");
            }

            switch (meleeWinnerResult.Winner)
            {
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(context.DefendingUnit.Quality()));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(context.AttackingUnit.Quality()));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie:
                    throw new InvalidOperationException($"Somehow reached the {nameof(DetermineMoraleSaveNeededStage)} stage " +
                        "when melee results were a tie.");
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ToRollForMorale.Activate(context);
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
