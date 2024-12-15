
using System;

namespace FDG.Stages
{

    public class OfferStrikeBackStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnOfferAccepted;
        public StageBinding OnOfferRejected;

        public OfferStrikeBackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnOfferAccepted = new StageBinding(this);
            OnOfferRejected = new StageBinding(this);
        }

        public override void Enter(ICombatActionContext context)
        {
            GameContext.Log("Offering strikeback.");

            GameContext.GetHandler<IOfferStrikeBackHandler>().Handle(context, 
                () => MoveToStrikingBack(context), () =>  SkipStrikingBack(context));
        }

        private void MoveToStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders striking back.");
            OnOfferAccepted.Activate(context);
        }

        private void SkipStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders not striking back.");
            OnOfferRejected.Activate(context);
        }
    }

    public interface IOfferStrikeBackHandler
    {
        public void Handle(ICombatActionContext context, Action acceptStrikeBack, Action rejectStrikeBack);
    }
}