
namespace FDG.Stages
{

    public class MovementStage : ParentStage<IUnitActionContext, IMovementActionContext>
    {
        public StageBinding OnFinishedMovement;

        /// <summary>
        /// The player abandoned the move at the path prompt. Distinct from <see cref="OnFinishedMovement"/>
        /// because leaving through that binding registers a move distance and marks the unit as having
        /// moved; a move that never happened must leave the unit free to choose another action.
        /// </summary>
        public StageBinding BackToChooseAction;

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
            BackToChooseAction = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DefinePathStage(GameContext, this), out var definePath)
                .AddChild(new ApplyNonMovementTerrainEffectsStage(GameContext, this), out var applyEffects)
                .AddChild(new StrafingStage(GameContext, this), out var strafing)
                .AddChild(new CrossingAttackStage(GameContext, this), out var crossingAttack)
                .AddChild(new ExecuteMoveStage(GameContext, this), out var executeMove)
                .AddSibling(nameof(OnFinishedMovement), OnFinishedMovement, out string onFinishedMovement)
                .AddSibling(nameof(BackToChooseAction), BackToChooseAction, out string backToChooseEvent)
                .Build();

            startingChild = definePath;

            definePath.OnPathDefined.Bind(applyEffects);
            definePath.BackToChooseAction.Bind(backToChooseEvent);
            // Strafing runs before the move is committed, so move-through detection reads the path from each
            // model's start position; on a strafe it resolves hits, then movement executes.
            applyEffects.OnAppliedNonMovementTerrainEffects.Bind(strafing);
            strafing.OnStrafeResolved.Bind(crossingAttack);
            // #197 P10 Crossing Attack: same pre-commit move-through window as Strafing, auto-wound flavour.
            crossingAttack.OnCrossingResolved.Bind(executeMove);
            executeMove.OnMoveExecuted.Bind(onFinishedMovement);

            return dictionary;
        }

        protected override void ReconcileChildContextBeforeLeaving(IUnitActionContext selfContext, IMovementActionContext childContext)
        {
            base.ReconcileChildContextBeforeLeaving(selfContext, childContext);

            // Runs on every exit, including the back-out. An abandoned move submitted no path, so there is
            // no distance to reconcile and the unit must not be marked as having moved.
            if (childContext.MoveCancelled) return;

            if(childContext.TryGetMovementDistance(out var distance) == false)
            {
                throw new InvalidOperationException($"Indicated that the unit didn't move at the end of {nameof(MovementStage)}.");
            }
            // #290: the allowance is taken from the MOVE context, which computed it while every rule that
            // authorised the move was still granted. ExecuteMoveStage spends one-shot movement grants when
            // the move resolves, so the shoot gate can no longer re-derive this number for itself.
            selfContext.RegisterMoveFinished(distance, childContext.MaxModelAdvanceDistance);
        }
    }
}