
using System.Collections.Generic;

namespace FDG.Stages
{
    public class SwingMeleeWeaponStage : ParentStage<ICombatActionContext, ICombatMetadata>
    {
        private const string SWING_TO_CHILD_ENTRANCE_TRANSITION = "SwingToChildEntrance";

        private readonly ICombatMetadata _attackContext;

        public StageBinding FinishedSwinging;

        public SwingMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            
        }

        public override void Enter(ICombatActionContext context)
        {
            GameContext.Log("Swinging.");

            base.Enter(context);
        }

        protected override ICombatMetadata GetNewChildContext(ICombatActionContext contextSelf)
        {
            return contextSelf.ConsumeAttackIntoContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            FinishedSwinging = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<ICombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new DetermineHitRollNeededStage<ICombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<ICombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(FinishedSwinging), FinishedSwinging, out string finishedSwingingEvent)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(determineHitRollNeeded)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollsNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedSwingingEvent);

            return dictionary;
        }

    }
}
