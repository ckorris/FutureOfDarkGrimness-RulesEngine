
namespace FDG.Stages
{
    internal class DetermineCanKeepSwingingStage : StateBase<IMeleeContext>
    {
        public string ReturnToChooseWeaponTransitionName;
        public string OutOfWeaponsTransitionName;
        public string DefenderKilledTransitionName;

        private readonly IMeleeContext _meleeContext;

        public DetermineCanKeepSwingingStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
            _meleeContext = context;
        }

        public override void Enter()
        {
            base.Enter();

            int remainingWeaponCount = _meleeContext.AvailableWeapons.Count;

            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if (remainingWeaponCount == 0)
            {
                _meleeContext.Log("Has swung with all melee weapons.");
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
            if(remainingWeaponCount == 1)
            {
                _meleeContext.Log("Has 1 more weapon left to swing.");
            }
            else
            {
                _meleeContext.Log($"Has {remainingWeaponCount} more weapons left to swing.");
            }
            
            _meleeContext.ResetMeleeCombatMetadata();
            SignalCanKeepSwinging();
        }

        public void BindReturnToChooseWeapon(StateBase returnStage)
        {
            ReturnToChooseWeaponTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{returnStage.GetType()}";
            Bind(ReturnToChooseWeaponTransitionName, returnStage);
        }

        public void BindOutOfWeapons(StateBase stageAfterShooting)
        {
            OutOfWeaponsTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{stageAfterShooting.GetType()}";
            Bind(OutOfWeaponsTransitionName, stageAfterShooting);
        }

        public void BindDefenderKilled(StateBase stageWhenDefenderKilled)
        {
            DefenderKilledTransitionName = $"{nameof(DetermineCanKeepShootingStage)}_TO_{stageWhenDefenderKilled.GetType()}";
            Bind(DefenderKilledTransitionName, stageWhenDefenderKilled);
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
