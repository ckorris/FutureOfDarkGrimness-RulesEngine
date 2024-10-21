
using System;

namespace FDG.Stages
{

    public class OfferStrikeBackStage : StateBase<IMeleeContext>
    {
        public const string OFFER_STRIKE_BACK_ACCEPTED_TRANSITION =
            "OfferStrikeBackAccepted";

        public const string OFFER_STRIKE_BACK_REJECTED_TRANSITION =
            "OfferStrikeBackRejectd";

        public OfferStrikeBackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.Log("Offering strikeback.");

            Context.OfferStrikeBackHandler.Handle(Context, MoveToStrikingBack, SkipStrikingBack);
        }


        private void MoveToStrikingBack()
        {
            Context.Log("Defenders striking back.");

            SignalEvent(OFFER_STRIKE_BACK_ACCEPTED_TRANSITION);
        }

        private void SkipStrikingBack()
        {
            Context.Log("Defenders not striking back.");

            SignalEvent(OFFER_STRIKE_BACK_REJECTED_TRANSITION);
        }
    }

    public interface IOfferStrikeBackHandler 
    {
        public void Handle(IMeleeContext context, Action acceptStrikeBack, Action rejectStrikeBack);
    }
}