
namespace FDG.StateMachine
{

    public class ChargingUnitAttackStage : StateBase<IMeleeContext>
    {
        public const string CHARGING_UNIT_ATTACK_TO_OFFER_STRIKE_BACK_TRANSITION =
            "ChargingUnitAttackToOfferStrikeBack";

        public ChargingUnitAttackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Entered charging unit attack stage. Attacking. (Moving on for now.)");
            MoveToOfferStrikeBack();
        }

        private void MoveToOfferStrikeBack()
        {
            SignalEvent(CHARGING_UNIT_ATTACK_TO_OFFER_STRIKE_BACK_TRANSITION);
        }
    }

}