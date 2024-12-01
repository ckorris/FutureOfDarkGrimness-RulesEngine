
using System;

namespace FDG.Stages
{

    public class OfferStrikeBackStage : StageBase<IMeleeContext>
    {
        public StageBinding OnOfferAccepted;
        public StageBinding OnOfferRejected;

        public OfferStrikeBackStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            OnOfferAccepted = new StageBinding(this);
            OnOfferRejected = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            GameContext.Log("Offering strikeback.");

            GameContext.GetHandler<IOfferStrikeBackHandler>().Handle(context, 
                () => MoveToStrikingBack(context), () =>  SkipStrikingBack(context));
        }

        private void MoveToStrikingBack(IMeleeContext context)
        {
            GameContext.Log("Defenders striking back.");
            OnOfferAccepted.Activate(context);
        }

        private void SkipStrikingBack(IMeleeContext context)
        {
            GameContext.Log("Defenders not striking back.");
            OnOfferRejected.Activate(context);
        }
    }

    public interface IOfferStrikeBackHandler
    {
        public void Handle(IMeleeContext context, Action acceptStrikeBack, Action rejectStrikeBack);
    }
}