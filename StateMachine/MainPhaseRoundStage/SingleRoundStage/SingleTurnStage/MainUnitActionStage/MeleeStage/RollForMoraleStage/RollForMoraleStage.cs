using System;

namespace FDG.Stages
{
    public class RollForMoraleStage : StageBase<ICombatActionContext>
    {
        public const string ROLL_FOR_MORALE_PASSED_TRANSITION = "RollForMoralePassed";
        public const string ROLL_FOR_MORALE_FAILED_TRANSITION = "RollForMoraleFailed";

        public StageBinding OnMoralePassed;
        public StageBinding OnMoraleFailed;

        public RollForMoraleStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnMoralePassed = new StageBinding(this);
            OnMoraleFailed = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.QueryForResult(out DetermineMoraleSaveNeededResult determineMoraleSaveNeededResult) == false)
            {
                throw new InvalidOperationException($"{nameof(RollForMoraleStage)} reached but there was no " +
                    $"{typeof(DetermineMoraleSaveNeededResult)} in the context metadata.");
            }

            // Morale roll modifiers (#021) and the Fearless re-roll are applied by the shared, rule-aware
            // morale test, which also resolves the decisive die (#090). This stage just routes the outcome.
            IUnit testingUnit = determineMoraleSaveNeededResult.LosingUnit.GetValue();
            MoraleUtilities.MoraleTestOutcome outcome = MoraleUtilities.TakeMoraleTest(
                GameContext, testingUnit, determineMoraleSaveNeededResult.RollNeeded);

            if (outcome.Passed)
            {
                if (outcome.PassedViaReroll)
                    GameContext.Log("Morale test failed but passed the Fearless re-roll (4+).");
                else
                    GameContext.Log($"Morale test passed (needed {outcome.RollNeeded}).");
                OnMoralePassed.Activate(context);
            }
            else
            {
                GameContext.Log($"Morale test failed (needed {outcome.RollNeeded}).");
                OnMoraleFailed.Activate(context);
            }
        }
    }
}
