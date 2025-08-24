

using FDG.Data;
using System.Collections.Generic;
using System.Numerics;

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

        public Dictionary<PlayerID, List<DataBinding<UnitData>>> UndeployedUnits { get; }

        public DataBinding<UnitData>? CurrentDeployingUnit { get; set; }  

    }

    public class DeploymentTurnContext : IDeploymentTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public IReadOnlyList<ITeam> FirstDeploymentRollOrder { get; }

        public IReadOnlyDictionary<ITeam, DataBinding<RectangularZone>> PlayerDeploymentZones { get; }

        public int CurrentDeployingTeamIndex { get; set; }

        public Dictionary<ITeam, int> CurrentDeployingPlayerIndexPerTeam { get; }

        public bool HasStarted { get; set; } = false;

        public Dictionary<PlayerID, List<DataBinding<UnitData>>> UndeployedUnits { get; }

        public DataBinding<UnitData>? CurrentDeployingUnit { get; set; } = null;

        public DeploymentTurnContext(IGameContext gameContext, List<ITeam> firstDeploymentRollOrder,
            Dictionary<ITeam, DataBinding<RectangularZone>> playerDeploymentZones)
        {
            GameContext = gameContext;
            FirstDeploymentRollOrder = firstDeploymentRollOrder;
            PlayerDeploymentZones = playerDeploymentZones;

            UndeployedUnits = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            CurrentDeployingPlayerIndexPerTeam = new Dictionary<ITeam, int>();

            List<ArmyData> armies = GameContext.GameDataStore().GetAllValues<ArmyData>().ToList();

            foreach (ITeam team in firstDeploymentRollOrder)
            {
                CurrentDeployingPlayerIndexPerTeam.Add(team, 0);

                foreach (PlayerID playerID in team.Players)
                {
                    List<DataBinding<UnitData>> playerUnits = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(playerID)))
                    {
                        playerUnits.AddRange(army.UnitBindings);
                    }
                    UndeployedUnits.Add(playerID, playerUnits);
                }
            }
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
            foreach(PlayerID playerID in team.Players)
            {
                if(DoesPlayerHaveRemainingDeployments(playerID))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DoesPlayerHaveRemainingDeployments(PlayerID playerID)
        {
            return UndeployedUnits[playerID].Count > 0;
        }
    }
}
