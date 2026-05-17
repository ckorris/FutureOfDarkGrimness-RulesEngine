namespace FDG.Stages
{
    public class MapSetupStage : ParentStage<IGameContext, IMapSetupContext>
    {
        public StageBinding ToDeployment;

        public MapSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        protected override IMapSetupContext GetNewChildContext(IGameContext contextSelf)
            => new MapSetupContext(contextSelf);

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMapSetupContext> startingChild)
        {
            ToDeployment = new StageBinding(this);

            var dictionary = new TransitionSetBuilder(this)
                .AddChild(new RollForFirstTerrainPlacementStage(GameContext, this), out var rollForFirstTerrain)
                .AddChild(new PlaceTerrainStage(GameContext, this), out var placeTerrain)
                .AddChild(new RollForObjectiveCountStage(GameContext, this), out var rollForObjectiveCount)
                .AddChild(new RollForFirstObjectivePlacementStage(GameContext, this), out var rollForFirstObjective)
                .AddChild(new PlaceObjectivesStage(GameContext, this), out var placeObjectives)
                .AddSibling(nameof(ToDeployment), ToDeployment, out string toDeploymentEvent)
                .Build();

            startingChild = rollForFirstTerrain;

            rollForFirstTerrain.OnRollComplete.Bind(placeTerrain);
            placeTerrain.OnTerrainPlaced.Bind(rollForObjectiveCount);
            rollForObjectiveCount.OnRollComplete.Bind(rollForFirstObjective);
            rollForFirstObjective.OnRollComplete.Bind(placeObjectives);
            placeObjectives.OnObjectivesPlaced.Bind(toDeploymentEvent);

            return dictionary;
        }
    }
}
