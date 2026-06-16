using FDG.Data;
using FDG.Rules.Dispatch;
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
                // #006: the losing unit tests morale at a living joined hero's Quality, if present.
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(
                        HeroStatRules.GetMoraleQuality(context.DefendingUnit.GetValue()), context.DefendingUnit));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(
                        HeroStatRules.GetMoraleQuality(context.AttackingUnit.GetValue()), context.AttackingUnit));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie:
                    throw new InvalidOperationException($"Somehow reached the {nameof(DetermineMoraleSaveNeededStage)} stage " +
                        "when melee results were a tie.");
                default:
                    throw new ArgumentOutOfRangeException();
            }

            await ToRollForMorale.Activate(context);
        }
    }

    public readonly struct DetermineMoraleSaveNeededResult
    {
        public readonly int RollNeeded;

        /// <summary>
        /// The unit that lost the melee and must take the morale test — the one a failed
        /// roll makes Shaken or Routs. Carried here so the outcome stage doesn't have to
        /// re-derive the loser from the winner result.
        /// </summary>
        public readonly DataBinding<UnitData> LosingUnit;

        public DetermineMoraleSaveNeededResult(int rollNeeded, DataBinding<UnitData> losingUnit)
        {
            RollNeeded = rollNeeded;
            LosingUnit = losingUnit;
        }
    }
}
