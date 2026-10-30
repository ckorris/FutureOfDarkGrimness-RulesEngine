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
                // #421: the losing unit tests morale at the best Quality on offer - its own, its living un-Shaken
                // joined hero's (#006), or a friendly Tough(9)+ Hero's within 6" - resolved here, immediately
                // before RollForMoraleStage rolls it, so the aura is measured at test time.
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(
                        MoraleUtilities.ResolveMoraleQuality(GameContext, context.DefendingUnit.GetValue()),
                        context.DefendingUnit));
                    break;
                case DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon:
                    context.AddResult(new DetermineMoraleSaveNeededResult(
                        MoraleUtilities.ResolveMoraleQuality(GameContext, context.AttackingUnit.GetValue()),
                        context.AttackingUnit));
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
        /// #421: <see cref="RollNeeded"/> with where it came from (the unit's own Quality, its joined hero's, or
        /// a Tough(9)+ Hero's aura), which <see cref="RollForMoraleStage"/> hands to the morale test.
        /// </summary>
        public readonly MoraleUtilities.MoraleQuality Quality;

        /// <summary>
        /// The unit that lost the melee and must take the morale test — the one a failed
        /// roll makes Shaken or Routs. Carried here so the outcome stage doesn't have to
        /// re-derive the loser from the winner result.
        /// </summary>
        public readonly DataBinding<UnitData> LosingUnit;

        public DetermineMoraleSaveNeededResult(int rollNeeded, DataBinding<UnitData> losingUnit)
            : this(MoraleUtilities.MoraleQuality.Own(rollNeeded), losingUnit)
        {
        }

        public DetermineMoraleSaveNeededResult(MoraleUtilities.MoraleQuality quality, DataBinding<UnitData> losingUnit)
        {
            RollNeeded = quality.Quality;
            Quality = quality;
            LosingUnit = losingUnit;
        }
    }
}
