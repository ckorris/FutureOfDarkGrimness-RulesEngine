
using System;

namespace FDG.Stages
{

    public class OfferStrikeBackStage : StateBase<IMeleeContext>
    {
        public const string OFFER_STRIKE_BACK_TO_STRIKE_BACK_TRANSITION =
            "OfferStrikeBackToStrikeBack";

        public const string OFFER_STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION =
            "OfferStrikeBackToResolveMeleeMorale";

        public OfferStrikeBackStage(StateMachine stateMachine, IMeleeContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            Context.OfferStrikeBackHandler.Handle(Context, MoveToStrikeBack, MoveToResolveMeleeMorale);
        }


        private void MoveToStrikeBack()
        {
            SignalEvent(OFFER_STRIKE_BACK_TO_STRIKE_BACK_TRANSITION);
        }

        private void MoveToResolveMeleeMorale()
        {
            SignalEvent(OFFER_STRIKE_BACK_TO_RESOLVE_MELEE_MORALE_TRANSITION);
        }

    }

    public interface IOfferStrikeBackHandler 
    {
        public void Handle(IMeleeContext context, Action acceptStrikeBack, Action rejectStrikeBack);
    }
}