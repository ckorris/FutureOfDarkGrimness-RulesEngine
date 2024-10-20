
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
