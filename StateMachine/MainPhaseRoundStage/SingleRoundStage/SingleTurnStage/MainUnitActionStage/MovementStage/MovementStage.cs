
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
                .AddChild(new StrafingStage(GameContext, this), out var strafing)
                .AddChild(new ExecuteMoveStage(GameContext, this), out var executeMove)
                .AddSibling(nameof(OnFinishedMovement), OnFinishedMovement, out string onFinishedMovement)
                .Build();

            startingChild = definePath;

            definePath.OnPathDefined.Bind(applyEffects);
            // Strafing runs before the move is committed, so move-through detection reads the path from each
            // model's start position; on a strafe it resolves hits, then movement executes.
            applyEffects.OnAppliedNonMovementTerrainEffects.Bind(strafing);
            strafing.OnStrafeResolved.Bind(executeMove);
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