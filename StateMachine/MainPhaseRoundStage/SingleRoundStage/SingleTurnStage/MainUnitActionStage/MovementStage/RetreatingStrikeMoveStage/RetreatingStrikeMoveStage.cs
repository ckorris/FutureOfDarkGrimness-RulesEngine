using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #381 (AoF Retreating Strike), move-action arm: after <see cref="ExecuteMoveStage"/> commits the
    /// unit's own move, fire the <see cref="EHookID.Movement_OnMoveResolved"/> hook and resolve any
    /// move-end strike through <see cref="RetreatingStrikeResolution"/>. On a hit, the successes run the
    /// save-skipping assign -> apply child pipeline (the <see cref="CrossingAttackStage"/> shape:
    /// unsaveable, Regeneration/Tough still apply).
    ///
    /// A declined/zero-length move is NOT a move (#333 doctrine - "Skip all" submits a real path of zero
    /// length), so the hook stays dark below the same distance floor MovedThisRound uses. The post-combat
    /// (Harassing-family) arm is <see cref="RetreatingStrikePostCombatStage"/>; the charger's forced 1"
    /// move-back is deliberately NO arm at all - see the #381 owner ruling on
    /// <see cref="Rules.Dispatch.Contexts.MoveResolvedContext"/>.
    /// </summary>
    public class RetreatingStrikeMoveStage : ParentStage<IMovementActionContext, ICombatMetadata>
    {
        public StageBinding OnStrikeResolved;

        // The accepted strike, computed in Enter and seeded into the child metadata. Only meaningful
        // between Enter and the child pipeline running (the CrossingAttackStage pattern).
        private RetreatingStrikeResolution.StrikeResult? _strike;

        public RetreatingStrikeMoveStage(IGameContext gameContext,
            IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(IMovementActionContext context)
        {
            _strike = null;

            // Same floor as MovementStage's MovedThisRound stamp: a submitted-but-empty path means the
            // unit chose not to move, and "ends its move" needs an actual move.
            if (!context.TryGetMovementDistance(out float distance) || distance <= 0.0001f)
            {
                await OnStrikeResolved.Activate(context);
                return;
            }

            _strike = await RetreatingStrikeResolution.OfferAndRoll(GameContext, context.MovingUnit);
            if (_strike == null)
            {
                await OnStrikeResolved.Activate(context);
                return;
            }

            // Run the save-skipping assign -> apply sub-pipeline.
            await base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(IMovementActionContext contextSelf)
        {
            return RetreatingStrikeResolution.BuildWoundMetadata(GameContext, contextSelf.MovingUnit,
                _strike!.Value);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(
            out StageBase<ICombatMetadata> startingChild)
        {
            OnStrikeResolved = new StageBinding(this);

            // No DetermineSaveRollsNeeded / RollToSave: the wounds are unsaveable, so the pipeline starts
            // at wound assignment (Regeneration/Tough) and applies.
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnStrikeResolved), OnStrikeResolved, out string strikeResolvedEvent)
                .Build();

            startingChild = assignWounds;

            assignWounds.BindNextStage(applyWounds)
                .BindToEvent(strikeResolvedEvent);

            return dictionary;
        }
    }
}
