
namespace FDG.Stages
{
    public interface IDeploymentContext : IGameContextAccessor
    {
        public List<ITeam>? MapSideRollOrder { get; }

        public List<ITeam>? FirstDeploymentRollOrder { get; }

        public Dictionary<ITeam, RectangularZone>? PlayerDeploymentZones {  get; }

        public void SetMapSideRollWinner(List<ITeam> mapSideRollOrder);

        public void SetFirstDeploymentRollWinner(List<ITeam> firstDeploymentRollOrder);
    }

    public class DeploymentContext : IDeploymentContext
    {
        public IGameContext GameContext { get; private set; }

        public List<ITeam>? MapSideRollOrder { get; private set; } = null;

        public List<ITeam>? FirstDeploymentRollOrder { get; private set; } = null;

        public Dictionary<ITeam, RectangularZone>? PlayerDeploymentZones { get; private set; } = null;

        public DeploymentContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void SetMapSideRollWinner(List<ITeam> mapSideRollOrder)
        {
            if(MapSideRollOrder != null)
            {
                throw new InvalidOperationException($"Tried to set roll winner order but it was already set.");
            }

            MapSideRollOrder = mapSideRollOrder;
        }

        public void SetDeploymentZones(Dictionary<ITeam, RectangularZone> playerDeploymentZones)
        {
            if(PlayerDeploymentZones != null)
            {
                throw new InvalidOperationException($"Tried to set player deployment zones but they were already set.");
            }

            PlayerDeploymentZones = playerDeploymentZones;
        }

        public void SetFirstDeploymentRollWinner(List<ITeam> firstDeploymentRollWinner)
        {
            if (FirstDeploymentRollOrder != null)
            {
                throw new InvalidOperationException($"Tried to set roll winner order, but it was already set.");
            }

            FirstDeploymentRollOrder = firstDeploymentRollWinner;
        }
    }
}
