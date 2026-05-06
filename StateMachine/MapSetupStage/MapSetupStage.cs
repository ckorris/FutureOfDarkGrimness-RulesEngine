
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

            var dictionary = new TransitionSetBuilder(this)
                .AddChild(new PlaceTerrainStage(GameContext, this), out var placeTerrain)
                .AddChild(new PlaceObjectivesStage(GameContext, this), out var placeObjectives)
                .AddSibling(nameof(ToDeployment), ToDeployment, out string toDeploymentEvent)
                .Build();

            startingChild = placeTerrain;

            placeTerrain.OnTerrainPlaced.Bind(placeObjectives);
            placeObjectives.OnObjectivesPlaced.Bind(toDeploymentEvent);

            return dictionary;
        }
    }
}
