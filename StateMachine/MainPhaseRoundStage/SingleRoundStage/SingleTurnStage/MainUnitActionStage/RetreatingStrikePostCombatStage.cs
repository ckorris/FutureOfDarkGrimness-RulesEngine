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
    /// Keys off <see cref="ICombatActionContext.PostCombatMover"/>, which the Post*Stages set ONLY when
    /// the gate detected a real reposition - a declined (zero-length) move stays dark, and so does a
    /// melee with no post-combat move at all: the charger's forced 1" move-back must not reach this hook
    /// (see <see cref="Rules.Dispatch.Contexts.MoveResolvedContext"/> for the ruling). Consumes the
    /// marker on entry so a stale value can never re-fire.
    /// </summary>
    public class RetreatingStrikePostCombatStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
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

            DataBinding<UnitData>? mover = context.PostCombatMover;
            context.PostCombatMover = null;

            if (mover == null)
            {
                await OnStrikeResolved.Activate(context);
                return;
            }

            _mover = mover;
            _strike = await RetreatingStrikeResolution.OfferAndRoll(GameContext, _mover);
            if (_strike == null)
            {
                await OnStrikeResolved.Activate(context);
                return;
            }

            // Run the save-skipping assign -> apply sub-pipeline.
            await base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            return RetreatingStrikeResolution.BuildWoundMetadata(GameContext, _mover, _strike!.Value);
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
