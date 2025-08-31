
using FDG.StageResolution.Requests;
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

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Offering strikeback.");

            //TODO: Indicate if they have struck back yet.
            YesNoRequest yesNoRequest = new YesNoRequest(context.DefendingUnit.PlayerID, "Strike back?");

            Task<bool> task = GameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(
                context.DefendingUnit.PlayerID, yesNoRequest);

            await task;

            if(task.Result)
            {
                MoveToStrikingBack(context);
            }
            else
            {
                SkipStrikingBack(context);
            }
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
}