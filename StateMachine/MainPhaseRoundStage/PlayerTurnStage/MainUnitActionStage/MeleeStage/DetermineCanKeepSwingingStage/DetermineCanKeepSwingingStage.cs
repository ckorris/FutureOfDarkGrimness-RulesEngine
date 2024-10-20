
namespace FDG.Stages
{
    internal class DetermineCanKeepSwingingStage : StateBase<IMeleeContext>
    {
        public string ReturnToChooseWeaponTransitionName;
        public string OutOfWeaponsTransitionName;
        public string DefenderKilledTransitionName;

        private readonly StateMachine _stateMachine;
        private readonly IMeleeContext _meleeContext;

        public DetermineCanKeepSwingingStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;
            _meleeContext = context;
        }

        public override void Enter()
        {
            base.Enter();

            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if (_meleeContext.AvailableWeapons.Count == 0)
            {
                _meleeContext.Log("Has fired all weapons.");
                SignalFinishedSwinging();
                return;
            }

            if (_meleeContext.MeleeCombatMetadata.DefendingUnit.RemainingWounds <= 0)
            {
                _meleeContext.Log("Has killed all target units.");
                SignalDefenderKilled();
                return;
            }

            //We've still got weapons to shoot, and baddies to shoot at. 
            _meleeContext.ResetMeleeCombatMetadata();
            SignalCanKeepSwinging();
        }

        public void BindReturnToChooseWeapon(StateBase returnStage)
        {
            ReturnToChooseWeaponTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{returnStage.GetType()}";
            _stateMachine.AddTransition<DetermineCanKeepShootingStage>(ReturnToChooseWeaponTransitionName, returnStage);
        }

        public void BindOutOfWeapons(StateBase stageAfterShooting)
        {
            OutOfWeaponsTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{stageAfterShooting.GetType()}";
            _stateMachine.AddTransition<DetermineCanKeepShootingStage>(OutOfWeaponsTransitionName, stageAfterShooting);
        }

        public void BindDefenderKilled(StateBase stageWhenDefenderKilled)
        {
            DefenderKilledTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{stageWhenDefenderKilled.GetType()}";
            _stateMachine.AddTransition<DetermineCanKeepShootingStage>(DefenderKilledTransitionName, stageWhenDefenderKilled);
        }

        private void SignalCanKeepSwinging()
        {
            SignalEvent(ReturnToChooseWeaponTransitionName);
        }

        private void SignalDefenderKilled()
        {
            SignalEvent(DefenderKilledTransitionName);
        }

        private void SignalFinishedSwinging()
        {
            SignalEvent(OutOfWeaponsTransitionName);
        }
    }
}
