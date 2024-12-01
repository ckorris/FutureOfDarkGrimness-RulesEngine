
namespace FDG.Stages
{

    public class StrikeBackStage : StageBase<IMeleeContext>
    {
        private const string STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION =
            "StrikeBackToChildEntrance";

        private readonly StateMachine _stateMachine;

        private DetermineCanKeepSwingingStage _determineCanKeepSwingingStage;

        private IMeleeContext _reversedContext;

        public StrikeBackStage(StateMachine stateMachine, IMeleeContext context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;

            //This is constructed much like a normal melee stage, but reduced. 
            _reversedContext = new MeleeContext(context.GameContext);

            ChooseMeleeWeaponStage chooseMeleeWeaponStage
                = new ChooseMeleeWeaponStage(stateMachine, _reversedContext, this);
            SwingMeleeWeaponStage swingMeleeWeaponStage
                = new SwingMeleeWeaponStage(stateMachine, _reversedContext, this);
            _determineCanKeepSwingingStage
                = new DetermineCanKeepSwingingStage(stateMachine, _reversedContext, this);

            Bind(STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION, chooseMeleeWeaponStage);

            chooseMeleeWeaponStage.Bind(ChooseMeleeWeaponStage.CHOOSE_MELEE_WEAPON_FINISHED_TRANSITION,
                swingMeleeWeaponStage);

            swingMeleeWeaponStage.AssignExitStage(_determineCanKeepSwingingStage);

            _determineCanKeepSwingingStage.BindReturnToChooseWeapon(chooseMeleeWeaponStage);
        }

        public void AssignNormalExitStage(StageBase targetStageWhenFinished)
        {
            _determineCanKeepSwingingStage.BindOutOfWeapons(targetStageWhenFinished);
        }

        public void AssignAttackerKilledExitStage(StageBase targetStageWhenAttackerKilled)
        {
            _determineCanKeepSwingingStage.BindDefenderKilled(targetStageWhenAttackerKilled);
        }

        public override void Enter()
        {
            base.Enter();

            _reversedContext.BeginNewAttack(GameContext.DefendingUnit, GameContext.AttackingUnit); //Purposefully reversed.

            MoveToChildStage();
        }

        private void MoveToChildStage()
        {
            SignalEvent(STRIKE_BACK_TO_CHILD_ENTRANCE_TRANSITION);
        }
    }
}