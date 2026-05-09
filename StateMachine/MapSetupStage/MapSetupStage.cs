
namespace FDG.Stages
{
    public class MapSetupStage : ParentStage<IGameContext, IGameContext>
    {
        public StageBinding ToDeployment;

        public MapSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        protected override IGameContext GetNewChildContext(IGameContext contextSelf) => contextSelf;

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IGameContext> startingChild)
        {
            ToDeployment = new StageBinding(this);

            int objectiveCount = 4; // fallback; overwritten by RollForObjectiveCountStage
            var dictionary = new TransitionSetBuilder(this)
                .AddChild(new RollForFirstTerrainPlacementStage(GameContext, this), out var rollForFirstTerrain)
                .AddChild(new PlaceTerrainStage(GameContext, this), out var placeTerrain)
                .AddChild(new RollForObjectiveCountStage(GameContext, this, count => objectiveCount = count), out var rollForObjectiveCount)
                .AddChild(new RollForFirstObjectivePlacementStage(GameContext, this), out var rollForFirstObjective)
                .AddChild(new PlaceObjectivesStage(GameContext, this, () => objectiveCount), out var placeObjectives)
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
