using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #381 (AoF Retreating Strike), post-combat arm: after the <see cref="PostCombatMoveGate"/> move
    /// (Harassing / Hit &amp; Run / Guerrilla family) actually repositioned a unit, fire the
    /// <see cref="EHookID.Movement_OnMoveResolved"/> hook for it and resolve any move-end strike through
    /// <see cref="RetreatingStrikeResolution"/>. Sits after <see cref="PostMeleeStage"/> in the melee
    /// chain AND after <see cref="PostShootStage"/> in the shoot chain - both funnels end at the same
    /// gate, so both get the same arm ("Harassing fires first, then the strike reads the final
    /// positions", the Discord-confirmed ordering in the #381 owner ruling).
    ///
    /// Keys off <see cref="ICombatActionContext.PostCombatMovers"/>, which the Post*Stages append ONLY
    /// when the gate detected a real reposition - a declined (zero-length) move stays dark, and so does
    /// a melee with no post-combat move at all: the charger's forced 1" move-back must not reach this
    /// hook (see <see cref="Rules.Dispatch.Contexts.MoveResolvedContext"/> for the ruling).
    ///
    /// #391: since both combatants may Harass, the movers DRAIN one strike per entry via the
    /// <see cref="ResolveMeleeReflectStage"/> loop pattern: the wound pipeline runs as a child stage
    /// once per entry, so <see cref="OnBatchDone"/> is bound back here by both parents and re-enters
    /// for the next mover; <see cref="OnStrikeResolved"/> continues the flow when the list is empty.
    /// </summary>
    public class RetreatingStrikePostCombatStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        /// <summary> One mover's strike resolved; bound back here to process the next. </summary>
        public StageBinding OnBatchDone;
        /// <summary> No movers left; continue the action flow. </summary>
        public StageBinding OnStrikeResolved;

        // The mover and its accepted strike, computed in Enter and seeded into the child metadata.
        // Only meaningful between Enter and the child pipeline running (the CrossingAttackStage pattern).
        private DataBinding<UnitData> _mover;
        private RetreatingStrikeResolution.StrikeResult? _strike;

        public RetreatingStrikePostCombatStage(IGameContext gameContext,
            IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ICombatActionContext context)
        {
            _strike = null;
            _mover = default;

            while (context.PostCombatMovers.Count > 0)
            {
                DataBinding<UnitData> mover = context.PostCombatMovers[0];
                context.PostCombatMovers.RemoveAt(0);

                RetreatingStrikeResolution.StrikeResult? strike =
                    await RetreatingStrikeResolution.OfferAndRoll(GameContext, mover);
                if (strike == null)
                {
                    continue;
                }

                // Run the save-skipping assign -> apply sub-pipeline for this mover; OnBatchDone
                // re-enters for any mover still queued.
                _mover = mover;
                _strike = strike;
                await base.Enter(context);
                return;
            }

            await OnStrikeResolved.Activate(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            return RetreatingStrikeResolution.BuildWoundMetadata(GameContext, _mover, _strike!.Value);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(
            out StageBase<ICombatMetadata> startingChild)
        {
            OnBatchDone = new StageBinding(this);
            OnStrikeResolved = new StageBinding(this);

            // No DetermineSaveRollsNeeded / RollToSave: the wounds are unsaveable, so the pipeline starts
            // at wound assignment (Regeneration/Tough) and applies. It ends at OnBatchDone (not the
            // exit): the parents bind that back here so a second mover gets its own pass (#391).
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnBatchDone), OnBatchDone, out string batchDoneEvent)
                .AddSibling(nameof(OnStrikeResolved), OnStrikeResolved, out string _)
                .Build();

            startingChild = assignWounds;

            assignWounds.BindNextStage(applyWounds)
                .BindToEvent(batchDoneEvent);

            return dictionary;
        }
    }
}
