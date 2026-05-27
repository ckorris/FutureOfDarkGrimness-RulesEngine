namespace FDG.Stages
{
    public interface IMapSetupContext : IGameContextAccessor
    {
        int? ObjectiveCount { get; }

        IReadOnlyList<ITeam>? ObjectivePlacementTeamOrder { get; }

        IReadOnlyList<ITeam>? TerrainPlacementTeamOrder { get; }

        void SetObjectiveCount(int count);

        void SetObjectivePlacementTeamOrder(List<ITeam> order);

        void SetTerrainPlacementTeamOrder(List<ITeam> order);
    }

    public class MapSetupContext : IMapSetupContext
    {
        public IGameContext GameContext { get; }

        public int? ObjectiveCount { get; private set; }

        public IReadOnlyList<ITeam>? ObjectivePlacementTeamOrder => _objectivePlacementTeamOrder;

        public IReadOnlyList<ITeam>? TerrainPlacementTeamOrder => _terrainPlacementTeamOrder;

        private List<ITeam>? _objectivePlacementTeamOrder;
        private List<ITeam>? _terrainPlacementTeamOrder;

        public MapSetupContext(IGameContext gameContext)
        {
            GameContext = gameContext;
        }

        public void SetObjectiveCount(int count)
        {
            if (ObjectiveCount.HasValue)
                throw new InvalidOperationException("Objective count was already set.");
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), $"Objective count must be positive (got {count}).");
            ObjectiveCount = count;
        }

        public void SetObjectivePlacementTeamOrder(List<ITeam> order)
        {
            if (_objectivePlacementTeamOrder != null)
                throw new InvalidOperationException("Objective placement team order was already set.");
            if (order == null || order.Count == 0)
                throw new ArgumentException("Team order must contain at least one team.", nameof(order));
            _objectivePlacementTeamOrder = order;
        }

        public void SetTerrainPlacementTeamOrder(List<ITeam> order)
        {
            if (_terrainPlacementTeamOrder != null)
                throw new InvalidOperationException("Terrain placement team order was already set.");
            if (order == null || order.Count == 0)
                throw new ArgumentException("Team order must contain at least one team.", nameof(order));
            _terrainPlacementTeamOrder = order;
        }
    }
}
