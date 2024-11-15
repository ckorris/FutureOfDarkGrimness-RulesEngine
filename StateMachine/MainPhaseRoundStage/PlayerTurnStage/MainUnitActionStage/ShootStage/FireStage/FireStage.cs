
namespace FDG.Stages
{

    public class FireStage : StateBase<IRangedContext>
    {
        public const string FIRE_TO_CHILD_ENTRANCE_TRANSITION =
            "FireToChildEntrance";

        private readonly StateMachine _stateMachine;
        private readonly SingleRangedAttackContext _attackContext;
        private readonly ApplyWoundsStage _applyWoundsStage;

        public FireStage(StateMachine stateMachine, IRangedContext context, StateBase parentState = null)
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

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _applyWoundsStage.BindNextStage(targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Firing.");

            //Reset context objects.
            _attackContext.SetCombatMetadata(Context.RangedCombatMetadata);

            MoveToChildBuildTargetListStage();
        }

        private void MoveToChildBuildTargetListStage()
        {
            SignalEvent(FIRE_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}