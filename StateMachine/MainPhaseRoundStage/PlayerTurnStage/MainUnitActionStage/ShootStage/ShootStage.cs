
namespace FDG.Stages
{

    public class ShootStage : StateBase<IUnitActionContext>
    {
        public const string SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION = "ShootToChildChooseRangedWeapon";

        private readonly StateMachine _stateMachine;

        public ShootStage(StateMachine stateMachine, IUnitActionContext context, IRangedContext rangedContext,
            StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            ChooseRangedWeaponStage chooseRangedWeaponStage = new ChooseRangedWeaponStage(stateMachine, rangedContext, this);
            ChooseRangedTargetStage chooseRangedTargetStage = new ChooseRangedTargetStage(stateMachine, rangedContext, this);
            FireStage fireStage = new FireStage(stateMachine, rangedContext, this);
            DetermineCanKeepShootingStage determineCanKeepShootingStage = new DetermineCanKeepShootingStage(stateMachine, rangedContext, this);
            ResolveRangedMoraleStage resolveRangedMoraleStage = new ResolveRangedMoraleStage(stateMachine, rangedContext, this);

           
            stateMachine.AddTransition<ShootStage>(SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION, chooseRangedWeaponStage);
            stateMachine.AddTransition<ChooseRangedWeaponStage>(ChooseRangedWeaponStage.CHOOSE_RANGED_WEAPON_TO_CHOOSE_RANGED_TARGET_TRANSITION,
                chooseRangedTargetStage);
            stateMachine.AddTransition<ChooseRangedTargetStage>(ChooseRangedTargetStage.CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION,
                fireStage);
            fireStage.AssignExitStage(determineCanKeepShootingStage);
            determineCanKeepShootingStage.BindReturnToChooseWeapon(chooseRangedWeaponStage);
            determineCanKeepShootingStage.BindFinishShooting(resolveRangedMoraleStage);
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _stateMachine.AddTransition<ResolveRangedMoraleStage>(ResolveRangedMoraleStage.RESOLVE_RANGED_MORALE_FINISHED_TRANSITION,
                targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.TextOutput.Log($"Shoot stage entering child: Choose Ranged Target.");

            MoveToChildChooseRangedWeapon();
        }

        private void MoveToChildChooseRangedWeapon()
        {
            SignalEvent(SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION);
        }
    }
}