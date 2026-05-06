namespace FDG.Stages
{
    public class PlaceObjectivesStage : StageBase<IGameContext>
    {
        public StageBinding OnObjectivesPlaced;

        public PlaceObjectivesStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            OnObjectivesPlaced = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            //TODO: Implement. Should place objective markers on the table.
            context.Log($"Entered {nameof(PlaceObjectivesStage)}.");

            OnObjectivesPlaced.Activate(context);
        }
    }
}
