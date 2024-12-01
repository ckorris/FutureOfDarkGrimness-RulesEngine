
using System.Collections.Generic;

namespace FDG.Stages
{
    public class SwingMeleeWeaponStage : ParentStage<IMeleeContext, ISingleAttackContext<IMeleeCombatMetadata>>
    {
        private const string SWING_TO_CHILD_ENTRANCE_TRANSITION = "SwingToChildEntrance";

        private readonly SingleMeleeAttackContext _attackContext;

        public StageBinding FinishedSwinging;
        public SwingMeleeWeaponStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            FinishedSwinging = new StageBinding(this);
        }

        /*
        public SwingMeleeWeaponStage(StateMachine stateMachine, IMeleeContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;
            _attackContext = new SingleMeleeAttackContext(context.GameContext);

            BuildTargetListStage buildTargetListStage = new BuildTargetListStage(stateMachine, _attackContext, this);
            _applyWoundsStage = new ApplyWoundsStage(stateMachine, _attackContext, this);

            buildTargetListStage.BindNextStage(new DetermineHitRollNeededStage(stateMachine, _attackContext, this))
                .BindNextStage(new RollToHitStage(stateMachine, _attackContext, this))
                .BindNextStage(new DetermineSaveRollsNeededStage(stateMachine, _attackContext, this))
                .BindNextStage(new RollToSaveStage(stateMachine, _attackContext, this))
                .BindNextStage(new AssignWoundsStage(stateMachine, _attackContext, this))
                .BindNextStage(_applyWoundsStage);

            //Set up transition to child stage.
            Bind(SWING_TO_CHILD_ENTRANCE_TRANSITION, buildTargetListStage);
        }
        */

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Swinging.");

            base.Enter(context);
        }

        protected override ISingleAttackContext<IMeleeCombatMetadata> GetNewChildContext(IMeleeContext contextSelf)
        {
            return new SingleMeleeAttackContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ISingleAttackContext<IMeleeCombatMetadata>> startingChild)
        {
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
