
using System.Collections.Generic;

namespace FDG.Stages
{
    public class SwingMeleeWeaponStage : ParentStage<IMeleeContext, IMeleeCombatMetadata>
    {
        private const string SWING_TO_CHILD_ENTRANCE_TRANSITION = "SwingToChildEntrance";

        private readonly IMeleeCombatMetadata _attackContext;

        public StageBinding FinishedSwinging;

        public SwingMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Swinging.");

            base.Enter(context);
        }

        protected override IMeleeCombatMetadata GetNewChildContext(IMeleeContext contextSelf)
        {
            return contextSelf.ConsumeAttackIntoContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMeleeCombatMetadata> startingChild)
        {
            FinishedSwinging = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<IMeleeCombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new DetermineHitRollNeededStage<IMeleeCombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<IMeleeCombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<IMeleeCombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<IMeleeCombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<IMeleeCombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<IMeleeCombatMetadata>(GameContext, this), out var applyWounds)
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
