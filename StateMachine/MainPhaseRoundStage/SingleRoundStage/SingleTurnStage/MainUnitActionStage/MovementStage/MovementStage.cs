using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

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

        /// <summary>
        /// #333: how far the unit must actually travel before the move counts as a move for
        /// <see cref="TokenType.MovedThisRound"/>. A float epsilon, not a game rule - the recorded distance
        /// is a sum of 2D hops, so a path of waypoints placed on the models' own positions can land a hair
        /// off zero. Any deliberate move clears it by orders of magnitude.
        /// </summary>
        private const float MOVED_TOKEN_MIN_DISTANCE_INCHES = 0.0001f;

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
                .AddChild(new RetreatingStrikeMoveStage(GameContext, this), out var retreatingStrike)
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
            // #381: the move-end strike hook (Movement_OnMoveResolved) fires once the positions are
            // committed - the "ends its move" seam for AoF Retreating Strike's own-move arm.
            executeMove.OnMoveExecuted.Bind(retreatingStrike);
            retreatingStrike.OnStrikeResolved.Bind(onFinishedMovement);

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

            // #333: a 0" move is not a move. "Skip all" (and a Done with no waypoints placed) submits a
            // real path of zero length, so it leaves through OnFinishedMovement rather than the back-out
            // above - and stamping there would switch a Mobile Artillery piece's defensive bonus off for
            // the round without it having moved an inch. The same false positive the MoveCancelled guard
            // prevents, reached through the other exit. The Move ACTION is still spent (RegisterMoveFinished
            // ran above): declining to move is a choice the unit made with its move, not a free hold.
            if (distance <= MOVED_TOKEN_MIN_DISTANCE_INCHES) return;

            // #197 Mobile Artillery: the same fact, but round-scoped and on the UNIT, so a rule can read it
            // from the far side of the table during someone else's activation (HasMoved above is
            // per-activation and only legible to the mover's own stages). This is the one seam a declared
            // move has actually resolved at - the back-out returned above, so a cancelled move never
            // stamps. Idempotent: a second move in the same round finds the token already there.
            IUnit movedUnit = selfContext.ActivatingUnit.GetValue();
            if (!movedUnit.Tokens.HasToken(TokenType.MovedThisRound))
            {
                movedUnit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));
            }
        }
    }
}