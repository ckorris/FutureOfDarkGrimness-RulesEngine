
namespace FDG.Stages
{

    public class MovementStage : ParentStage<IUnitActionContext, IMovementActionContext>
    {
        public StageBinding OnFinishedMovement;

        public MovementStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent) : base(gameContext, parent)
        {
            
        }


        protected override IMovementActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new MovementActionContext(GameContext, contextSelf.ActivatingUnit);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMovementActionContext> startingChild)
        {
            OnFinishedMovement = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DefinePathStage(GameContext, this), out var definePath)
                .AddChild(new ApplyNonMovementTerrainEffectsStage(GameContext, this), out var applyEffects)
                .AddChild(new ExecuteMoveStage(GameContext, this), out var executeMove)
                .AddSibling(nameof(OnFinishedMovement), OnFinishedMovement, out string onFinishedMovement)
                .Build();

            startingChild = definePath;

            definePath.OnPathDefined.Bind(applyEffects);
            applyEffects.OnAppliedNonMovementTerrainEffects.Bind(executeMove);
            executeMove.OnMoveExecuted.Bind(onFinishedMovement);

            return dictionary;
        }

        protected override void ReconcileChildContextBeforeLeaving(IUnitActionContext selfContext, IMovementActionContext childContext)
        {
            base.ReconcileChildContextBeforeLeaving(selfContext, childContext);

            if(childContext.TryGetMovementDistance(out var distance) == false)
            {
                throw new InvalidOperationException($"Indicated that the unit didn't move at the end of {nameof(MovementStage)}.");
            }
            selfContext.RegisterMoveFinished(distance);
        }
    }
}