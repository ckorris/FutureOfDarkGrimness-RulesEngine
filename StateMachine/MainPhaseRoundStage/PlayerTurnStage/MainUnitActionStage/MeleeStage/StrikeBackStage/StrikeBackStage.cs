
namespace FDG.Stages
{

    public class StrikeBackStage : StateBase<IMeleeContext>
    {
        private const string STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION =
            "StrikeBackToChildEntrance";

        private readonly StateMachine _stateMachine;

        private DetermineCanKeepSwingingStage _determineCanKeepSwingingStage;

        public StrikeBackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            //This is constructed much like a normal melee stage, but reduced. 
            IMeleeContext reversedContext = new MeleeContext(context.ChooseMeleeWeaponHandler, context.OfferStrikeBackHandler,
                context.TextOutput, context.DiceRoller);

            ChooseMeleeWeaponStage chooseMeleeWeaponStage
                = new ChooseMeleeWeaponStage(stateMachine, reversedContext, this);
            SwingMeleeWeaponStage swingMeleeWeaponStage
                = new SwingMeleeWeaponStage(stateMachine, reversedContext, this);
            _determineCanKeepSwingingStage
                = new DetermineCanKeepSwingingStage(stateMachine, reversedContext, this);

            StateMachine.AddTransition<ChooseMeleeWeaponStage>(ChooseMeleeWeaponStage.CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION,
                swingMeleeWeaponStage);

            swingMeleeWeaponStage.AssignExitStage(_determineCanKeepSwingingStage);

            _determineCanKeepSwingingStage.BindReturnToChooseWeapon(chooseMeleeWeaponStage);
        }

        public void AssignNormalExitStage(StateBase targetStageWhenFinished)
        {
            _determineCanKeepSwingingStage.BindOutOfWeapons(targetStageWhenFinished);
        }

        public void AssignAttackerKilledExitStage(StateBase targetStageWhenAttackerKilled)
        {
            _determineCanKeepSwingingStage.BindDefenderKilled(targetStageWhenAttackerKilled);
        }

        public override void Enter()
        {
            base.Enter();

            throw new NotImplementedException();

            MoveToChildBuildTargetListStage();
        }

        private void MoveToChildBuildTargetListStage()
        {
            SignalEvent(STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}