
using FDG.Data;

namespace FDG.Stages
{
    public interface IMainPhaseContext : IGameContextAccessor
    {
        public int RoundCount { get; }

        public List<ITeam> TeamActivateOrder { get; }

        public void OnEndOfRound(IReadOnlyList<ITeam> newTeamActivateOrder);
    }

    public class MainPhaseContext : IMainPhaseContext
    {
        public IGameContext GameContext { get; private set; }

        public int RoundCount { get; private set; } = 1;

        public List<ITeam>? TeamActivateOrder { get; private set; }

        public MainPhaseContext(IGameContext gameContext, List<ITeam> firstDeploymentRollOrder)
        {
            GameContext = gameContext;

            //Slight hack: Since the first round player start order is based on deployment, but subsequent ones are based
            //on who finished first each round, we set the last round team finish order to the deployment roll order
            //in the constructor, so that the first call to NewRound() will use that data.
            TeamActivateOrder = firstDeploymentRollOrder;
        }

        // Restore constructor for save/load resume: seeds the round number and activation order from
        // a snapshot instead of the deployment roll-off.
        public MainPhaseContext(IGameContext gameContext, List<ITeam> teamActivateOrder, int roundCount)
        {
            GameContext = gameContext;
            TeamActivateOrder = teamActivateOrder;
            RoundCount = roundCount;
        }

        public void OnEndOfRound(IReadOnlyList<ITeam> newTeamActivateOrder)
        {
            /*
            List<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>().ToList();
            
            //TODO: I anticipate this happening if units were killed before activating.
            if (_unactivatedUnits.Count > 0)
            {
                throw new InvalidOperationException($"Expected {nameof(_unactivatedUnits)} to be empty when calling {nameof(NewRound)}." +
                    $"Instead it had {_unactivatedUnits.Count} entry/entries.");
            }

            foreach (ITeam team in GameContext.TableState.Teams.Objects)
            {
                if (_currentRoundTeamFinishOrder.Contains(team) == false)
                {
                    throw new InvalidOperationException($"Couldn't find team {team.TeamNumber} in finished teams during call to {nameof(NewRound)}. " +
                        $"Total teams listed as finished: {_currentRoundTeamFinishOrder.Count}");
                }
            }

            SetUnactivatedUnits();

            _teamActivateOrder = new List<ITeam>(_currentRoundTeamFinishOrder);
            _currentRoundTeamFinishOrder.Clear();
            */

            // The finish order decides who LEADS next round, but it is also the complete team list the next
            // round alternates over - so a team missing from it does not merely lose the lead, it loses every
            // activation for the rest of the game (each later round rebuilds the order from a finish order
            // that can no longer contain it). SingleRoundContext now records a team that runs out however it
            // ran out; this is the backstop, so no future path that empties a pool can silently delete a team
            // from the game. Anything the round did not report keeps its previous relative position.
            List<ITeam> merged = new List<ITeam>(newTeamActivateOrder);
            foreach (ITeam team in TeamActivateOrder ?? new List<ITeam>())
            {
                if (merged.Contains(team) == false)
                    merged.Add(team);
            }

            // Covers the old empty-finish-order case too (every unit already dead going in): merged falls
            // back to the previous order rather than handing an empty list to the next SingleRoundContext,
            // which would crash on the first TryAdvance.
            if (merged.Count > 0)
                TeamActivateOrder = merged;

            RoundCount++;
        }

        
    }
}