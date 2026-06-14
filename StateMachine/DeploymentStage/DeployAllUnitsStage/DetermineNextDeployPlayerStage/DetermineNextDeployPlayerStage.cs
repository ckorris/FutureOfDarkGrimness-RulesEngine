

namespace FDG.Stages
{
    public class DetermineNextDeployPlayerStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;
        public StageBinding OnFinishedDeployingAllUnits;

        public DetermineNextDeployPlayerStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
            OnFinishedDeployingAllUnits = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.Log("Entering Determine Next Deploy Player stage.");

            // First time through we keep the cursor's initial state (the roll-off winner).
            if (context.HasStarted == false)
            {
                context.HasStarted = true;
                await OnFinish.Activate(context);
                return;
            }

            bool advanced = context.Cursor.TryAdvance(
                teamHasRemainingWork: context.DoesTeamHaveRemainingDeployments,
                playerHasRemainingWork: context.DoesPlayerHaveRemainingDeployments,
                out _, out _);

            if (!advanced)
            {
                context.Log("All players have deployed.");
                await OnFinishedDeployingAllUnits.Activate(context);
                return;
            }

            await OnFinish.Activate(context);
        }
    }
}

