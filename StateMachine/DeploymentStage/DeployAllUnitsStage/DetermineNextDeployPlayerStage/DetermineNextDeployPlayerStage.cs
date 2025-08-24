

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

            if (context.HasStarted == false)
            {
                //If we haven't started yet, then the default values point to the first player. 
                //Indicate that we've started and then move along.
                context.HasStarted = true;
            }
            else
            {
                //Find the next team that has remaining deployments.
                //TODO: This could be reused for choosing the next player. Put into a utility?
                int startingTeamIndex = context.CurrentDeployingTeamIndex;
                int teamCount = context.FirstDeploymentRollOrder.Count(); //Shorthand.

                //Start with the logical next team. But we cache this one because if the loop brings us back to this one,
                //then everyone has gone and we're done.
                int firstTeamToCheck = (startingTeamIndex < teamCount - 1) ? startingTeamIndex + 1 : 0;
                ITeam nextTeam;

                int nextTeamIndex = firstTeamToCheck;

                while (true)
                {
                    nextTeam = context.FirstDeploymentRollOrder[nextTeamIndex]; //Reuse for less allocation.
                    if (context.DoesTeamHaveRemainingDeployments(nextTeam))
                    {
                        //We found a valid one. nextTeam is now the correct value.
                        context.CurrentDeployingTeamIndex = nextTeamIndex;
                        break;
                    }

                    nextTeamIndex = (nextTeamIndex < teamCount - 1) ? nextTeamIndex + 1 : 0;

                    if (nextTeamIndex == firstTeamToCheck)
                    {
                        //We've checked every team and there are none left, so we're done.
                        context.Log("All players have deployed.");
                        OnFinishedDeployingAllUnits.Activate(context);
                        return;
                    }
                }

                //Find the next player within the team to deploy.
                //Often, likely usually, teams have one players, so we don't always need to do this.
                if (nextTeam.Players.Count() == 1)
                {
                    OnFinish.Activate(context);
                    return;
                }

                int playerCount = nextTeam.Players.Count(); //Shorthand.

                int startingPlayerIndex = context.CurrentDeployingPlayerIndexPerTeam[nextTeam];

                int firstPlayerToCheckIndex = (startingPlayerIndex < playerCount - 1) ? startingPlayerIndex + 1 : 0;
                int nextPlayerIndex = firstPlayerToCheckIndex;
                PlayerID nextPlayer;

                while (true)
                {
                    nextPlayer = nextTeam.Players[nextPlayerIndex];
                    if (context.DoesPlayerHaveRemainingDeployments(nextPlayer))
                    {
                        context.CurrentDeployingPlayerIndexPerTeam[nextTeam] = nextPlayerIndex;
                        OnFinish.Activate(context);
                        return;
                    }

                    nextPlayerIndex = (nextPlayerIndex < playerCount - 1) ? nextPlayerIndex + 1 : 0;

                    //To avoid an infinite loop in case there's a bug in the code to find if a team has valid deployments.
                    if (nextPlayerIndex == firstPlayerToCheckIndex)
                    {
                        throw new InvalidOperationException("Couldn't find a player within a team with deployments left, but " +
                            $"that team was listed as having deployments by {nameof(IDeploymentTurnContext.DoesTeamHaveRemainingDeployments)}.");
                    }
                }
            }
        }
    }
}

