

using FDG.Data;

namespace FDG.Stages
{
    public interface IDeploymentTurnContext : IGameContextAccessor
    {
        bool HasStarted { get; set; }

        IReadOnlyList<ITeam> FirstDeploymentRollOrder { get; }

        IReadOnlyDictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones { get; }

        int CurrentDeployingTeamIndex { get; set; }

        Dictionary<ITeam, int> CurrentDeployingPlayerIndexPerTeam { get; }

        PlayerID GetCurrentDeployingPlayerID();

        public bool DoesTeamHaveRemainingDeployments(ITeam team);

        public bool DoesPlayerHaveRemainingDeployments(PlayerID playerID);

    }

    public class DeploymentTurnContext : IDeploymentTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyList<ITeam> FirstDeploymentRollOrder { get; }

        public IReadOnlyDictionary<ITeam, DataBinding<RectangularZone>> PlayerDeploymentZones { get; }

        public int CurrentDeployingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentDeployingPlayerIndexPerTeam => throw new NotImplementedException();

        public bool HasStarted { get; set; } = false;




        public DeploymentTurnContext(IGameContext gameContext, List<ITeam>? firstDeploymentRollOrder, 
            Dictionary<ITeam, DataBinding<RectangularZone>>? playerDeploymentZones)
        {
            GameContext = gameContext;
            FirstDeploymentRollOrder = firstDeploymentRollOrder;
            PlayerDeploymentZones = playerDeploymentZones;
        }

        public PlayerID GetCurrentDeployingPlayerID()
        {
            ITeam currentTeam = FirstDeploymentRollOrder[CurrentDeployingTeamIndex];
            int playerIndex = CurrentDeployingPlayerIndexPerTeam[currentTeam];
            PlayerID currentPlayerID = currentTeam.Players[playerIndex];

            return currentPlayerID;
        }

        public bool DoesTeamHaveRemainingDeployments(ITeam team)
        {
            throw new NotImplementedException();
        }

        public bool DoesPlayerHaveRemainingDeployments(PlayerID playerID)
        {
            throw new NotImplementedException();
        }
    }
}
