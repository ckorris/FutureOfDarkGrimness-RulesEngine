
namespace FDG.Stages
{

    public class DetermineCanKeepShootingStage : StateBase<IRangedContext>
    {
        public string ReturnToChooseWeaponTransitionName;
        public string FinishShootingTransitionName;

        private readonly StateMachine _stateMachine;
        private readonly IRangedContext _context;

        public DetermineCanKeepShootingStage(StateMachine stateMachine, IRangedContext context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;
            _context = context;
        }

        public override void Enter()
        {
            base.Enter();
            //TODO: If we've killed off the defender, leave.

            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if(_context.AvailableWeapons.Count == 0)
            {
                _context.Log("Has fired all weapons.");
                SignalFinishedShooting();
                return;
            }

            if(_context.RangedCombatMetadata.DefendingUnit.RemainingWounds <= 0)
            {
                _context.Log("Has killed all target units.");
                SignalFinishedShooting();
                return;
            }

            //We've still got weapons to shoot, and baddies to shoot at. 
            _context.ResetRangedCombatMetadata();
            SignalCanKeepShooting();
        }

        public void BindReturnToChooseWeapon(StateBase returnStage)
        {
            ReturnToChooseWeaponTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{returnStage.GetType()}";
            _stateMachine.AddTransition<DetermineCanKeepShootingStage>(ReturnToChooseWeaponTransitionName, returnStage);
        }

        public void BindFinishShooting(StateBase stageAfterShooting)
        {
            FinishShootingTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{stageAfterShooting.GetType()}";
            _stateMachine.AddTransition<DetermineCanKeepShootingStage>(FinishShootingTransitionName, stageAfterShooting);
        }

        private void SignalCanKeepShooting()
        {
            SignalEvent(ReturnToChooseWeaponTransitionName);
        }

        private void SignalFinishedShooting()
        {
            SignalEvent(FinishShootingTransitionName);
        }
    }

}