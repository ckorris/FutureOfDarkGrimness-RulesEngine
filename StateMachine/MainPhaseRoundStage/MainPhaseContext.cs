
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

            TeamActivateOrder = new List<ITeam>(newTeamActivateOrder);

            RoundCount++;
        }

        
    }
}