
namespace FDG.Stages
{
    public class RollForMoraleStage : StateBase<IMeleeContext>
    {
        public const string ROLL_FOR_MORALE_PASSED_TRANSITION = "RollForMoralePassed";
        public const string ROLL_FOR_MORALE_FAILED_TRANSITION = "RollForMoraleFailed";

        public RollForMoraleStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            //TODO: Modifiers need to be able to affect this roll. Can we make these combat stages?

            if (Context.QueryForResult(out DetermineMoraleSaveNeededResult determineMoraleSaveNeededResult) == false)
            {
                throw new InvalidOperationException($"{nameof(RollForMoraleStage)} reached but there was no " +
                    $"{typeof(DetermineMoraleSaveNeededResult)} in the context metadata.");
            }

            IDiceResults moraleRoll = Context.DiceRoller.Roll(1);

            int rollNeeded = determineMoraleSaveNeededResult.RollNeeded; //Shorthand.

            //TODO: Below, we can't really fragment passes and fails with the probabilistic dice roller. Figure out something for that.
            if (moraleRoll.AtOrAbove(rollNeeded) >= 1f)
            {
                Context.Log($"Morale test passed (needed {rollNeeded}).");
                MoveToMoralePassedStage();
            }
            else
            {
                Context.Log($"Morale test failed (needed {rollNeeded}).");
                MoveToMoraleFailedStage();
            }
        }

        private void MoveToMoralePassedStage()
        {
            SignalEvent(ROLL_FOR_MORALE_PASSED_TRANSITION);
        }

        private void MoveToMoraleFailedStage()
        {
            SignalEvent(ROLL_FOR_MORALE_FAILED_TRANSITION);
        }
    }
}
