

using FDG.Data;

namespace FDG.Stages
{
    public interface IDeploymentTurnContext : IGameContextAccessor
    {
        List<ITeam>? FirstDeploymentRollOrder { get; }

        Dictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones { get; }
    }

    public class DeploymentTurnContext : IDeploymentTurnContext
    {
        public IGameContext GameContext { get; private set; }

        public List<ITeam>? FirstDeploymentRollOrder { get; }

        public Dictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones { get; }

        public DeploymentTurnContext(IGameContext gameContext, List<ITeam>? firstDeploymentRollOrder, Dictionary<ITeam, DataBinding<RectangularZone>>? playerDeploymentZones)
        {
            GameContext = gameContext;
            FirstDeploymentRollOrder = firstDeploymentRollOrder;
            PlayerDeploymentZones = playerDeploymentZones;
        }
    }
}
