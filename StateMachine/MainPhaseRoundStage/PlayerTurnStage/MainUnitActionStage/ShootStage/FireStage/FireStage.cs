
using System.Collections.Generic;

namespace FDG.Stages
{

    public class FireStage : ParentStage<IRangedContext, ISingleAttackContext<IRangedCombatMetadata>>
    {
        public StageBinding OnFinishedFiring;

        public FireStage(IGameContext gameContext, IStateMachineLayer<IRangedContext> parent) : base(gameContext, parent)
        {
            OnFinishedFiring = new StageBinding(this);
        }

        /*
        public FireStage(StateMachine stateMachine, IRangedContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;
            _attackContext = new SingleRangedAttackContext(context.GameContext);

            BuildTargetListStage buildTargetListStage = new BuildTargetListStage(stateMachine, _attackContext, this);
            _applyWoundsStage = new ApplyWoundsStage(stateMachine, _attackContext, this);

            buildTargetListStage.BindNextStage(new RangeCheckStage(stateMachine, _attackContext, this))
                .BindNextStage(new OcclusionCheckStage(stateMachine, _attackContext, this))
                .BindNextStage(new CoverCheckStage(stateMachine, _attackContext, this))
                .BindNextStage(new DetermineHitRollNeededStage(stateMachine, _attackContext, this))
                .BindNextStage(new RollToHitStage(stateMachine, _attackContext, this))
                .BindNextStage(new DetermineSaveRollsNeededStage(stateMachine, _attackContext, this))
                .BindNextStage(new RollToSaveStage(stateMachine, _attackContext, this))
                .BindNextStage(new AssignWoundsStage(stateMachine, _attackContext, this))
                .BindNextStage(_applyWoundsStage);

            //Set up transition to child stage.
            Bind(FIRE_TO_CHILD_ENTRANCE_TRANSITION, buildTargetListStage);
        }
        */

        /*
        public override void Enter()
        {
            base.Enter();

            GameContext.Log("Firing.");

            //Reset context objects.
            _attackContext.SetCombatMetadata(GameContext.RangedCombatMetadata);

            MoveToChildBuildTargetListStage();
        }
        */

        public override void Enter(IRangedContext context)
        {
            GameContext.Log("Firing.");

            base.Enter(context);
        }

        protected override ISingleAttackContext<IRangedCombatMetadata> GetNewChildContext(IRangedContext contextSelf)
        {
            return new SingleRangedAttackContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ISingleAttackContext<IRangedCombatMetadata>> startingChild)
        {
            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new BuildTargetListStage<IRangedCombatMetadata>(GameContext, this), out var buildTargetList)
                .AddChild(new RangeCheckStage(GameContext, this), out var rangeCheck)
                .AddChild(new OcclusionCheckStage(GameContext, this), out var occlusionCheck)
                .AddChild(new CoverCheckStage(GameContext, this), out var coverCheck)
                .AddChild(new DetermineHitRollNeededStage<IRangedCombatMetadata>(GameContext, this), out var determineHitRollNeeded)
                .AddChild(new RollToHitStage<IRangedCombatMetadata>(GameContext, this), out var rollToHit)
                .AddChild(new DetermineSaveRollsNeededStage<IRangedCombatMetadata>(GameContext, this), out var determineSaveRollNeeded)
                .AddChild(new RollToSaveStage<IRangedCombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<IRangedCombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<IRangedCombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinishedFiring), OnFinishedFiring, out string finishedFiringName)
                .Build();

            startingChild = buildTargetList;

            buildTargetList.BindNextStage(rangeCheck)
                .BindNextStage(occlusionCheck)
                .BindNextStage(coverCheck)
                .BindNextStage(determineHitRollNeeded)
                .BindNextStage(rollToHit)
                .BindNextStage(determineSaveRollNeeded)
                .BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedFiringName);

            return dictionary;
        }
    }
}