
using FDG.Data;

namespace FDG.Stages
{
    public interface IDeploymentContext : IGameContextAccessor
    {
        List<ITeam>? MapSideRollOrder { get; }

        List<ITeam>? FirstDeploymentRollOrder { get; }

        Dictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones {  get; }

        void SetMapSideRollWinner(List<ITeam> mapSideRollOrder);

        void SetDeploymentZones(Dictionary<ITeam, DataBinding<RectangularZone>> playerDeploymentZones);
        
        void SetFirstDeploymentRollWinner(List<ITeam> firstDeploymentRollOrder);
    }

    public class DeploymentContext : IDeploymentContext
    {
        public IGameContext GameContext { get; private set; }

        public List<ITeam>? MapSideRollOrder { get; private set; } = null;

        public List<ITeam>? FirstDeploymentRollOrder { get; private set; } = null;

        public Dictionary<ITeam, DataBinding<RectangularZone>>? PlayerDeploymentZones { get; private set; } = null;

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

        public void SetDeploymentZones(Dictionary<ITeam, DataBinding<RectangularZone>> playerDeploymentZones)
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
