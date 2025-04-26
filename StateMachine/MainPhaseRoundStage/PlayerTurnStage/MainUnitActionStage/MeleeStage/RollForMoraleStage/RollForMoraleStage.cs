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
            //TODO: Modifiers need to be able to affect this roll. Can we make these combat stages?

            if (context.QueryForResult(out DetermineMoraleSaveNeededResult determineMoraleSaveNeededResult) == false)
            {
                throw new InvalidOperationException($"{nameof(RollForMoraleStage)} reached but there was no " +
                    $"{typeof(DetermineMoraleSaveNeededResult)} in the context metadata.");
            }

            IDiceResults moraleRoll = GameContext.GameContext.DiceRoller.Roll(1);

            int rollNeeded = determineMoraleSaveNeededResult.RollNeeded; //Shorthand.

            //TODO: Below, we can't really fragment passes and fails with the probabilistic dice roller. Figure out something for that.
            if (moraleRoll.AtOrAbove(rollNeeded) >= 1f)
            {
                GameContext.Log($"Morale test passed (needed {rollNeeded}).");
                OnMoralePassed.Activate(context);
            }
            else
            {
                GameContext.Log($"Morale test failed (needed {rollNeeded}).");
                OnMoraleFailed.Activate(context);
            }
        }
    }
}
