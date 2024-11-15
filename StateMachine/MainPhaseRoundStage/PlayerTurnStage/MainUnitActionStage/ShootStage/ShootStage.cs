
namespace FDG.Stages
{

    public class ShootStage : StateBase<IUnitActionContext>
    {
        public const string SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION = "ShootToChildChooseRangedWeapon";

        private readonly StateMachine _stateMachine;

        private readonly ResolveRangedMoraleStage _resolveRangedMoraleStage;

        public ShootStage(StateMachine stateMachine, IUnitActionContext context, IRangedContext rangedContext,
            StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            ChooseRangedWeaponStage chooseRangedWeaponStage = new ChooseRangedWeaponStage(stateMachine, rangedContext, this);
            ChooseRangedTargetStage chooseRangedTargetStage = new ChooseRangedTargetStage(stateMachine, rangedContext, this);
            FireStage fireStage = new FireStage(stateMachine, rangedContext, this);
            DetermineCanKeepShootingStage determineCanKeepShootingStage = new DetermineCanKeepShootingStage(stateMachine, rangedContext, this);
            _resolveRangedMoraleStage = new ResolveRangedMoraleStage(stateMachine, rangedContext, this);


            Bind(SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION, chooseRangedWeaponStage);
            chooseRangedWeaponStage.Bind(ChooseRangedWeaponStage.CHOOSE_RANGED_WEAPON_TO_CHOOSE_RANGED_TARGET_TRANSITION,
                chooseRangedTargetStage);
            chooseRangedTargetStage.Bind(ChooseRangedTargetStage.CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION,
                fireStage);
            fireStage.AssignExitStage(determineCanKeepShootingStage);
            determineCanKeepShootingStage.BindReturnToChooseWeapon(chooseRangedWeaponStage);
            determineCanKeepShootingStage.BindFinishShooting(_resolveRangedMoraleStage);
        }

        public void AssignExitStage(StateBase targetStageWhenFinished)
        {
            _resolveRangedMoraleStage.Bind(ResolveRangedMoraleStage.RESOLVE_RANGED_MORALE_FINISHED_TRANSITION,
                targetStageWhenFinished);
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log($"Shoot stage entering child: Choose Ranged Target.");

            //TODO: Reset metadata?

            MoveToChildChooseRangedWeapon();
        }

        private void MoveToChildChooseRangedWeapon()
        {
            SignalEvent(SHOOT_TO_CHILD_CHOOSE_RANGED_WEAPON_TRANSITION);
        }
    }
}