
namespace FDG.Stages
{
    public class SwingMeleeWeaponStage : StateBase<IMeleeContext>
    {
        private const string SWING_TO_CHILD_ENTRANCE_TRANSITION = "SwingToChildEntrance";

        private readonly StateMachine _stateMachine;
        private readonly SingleMeleeAttackContext _attackContext;
        private readonly ApplyWoundsStage _applyWoundsStage;

        public SwingMeleeWeaponStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
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

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _applyWoundsStage.BindNextStage(targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Swinging.");

            //Reset context objects.
            _attackContext.SetCombatMetadata(Context.MeleeCombatMetadata);

            MoveToChildBuildTargetListStage();
        }

        private void MoveToChildBuildTargetListStage()
        {
            SignalEvent(SWING_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}
