
using FDG.StageResolution.Requests;
using FDG.Utilities;
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
            // Skip if no in-range defending model has a melee weapon to fight back with (#017).
            if (MeleeRangeUtilities.GetMeleeWeaponsFromModels(context.InRangeDefendingModels).Count == 0)
            {
                GameContext.Log("No in-range defender with a melee weapon; skipping strikeback.");
                await SkipStrikingBack(context);
                return;
            }

            GameContext.Log("Offering strikeback.");

            //TODO: Indicate if they have struck back yet.
            YesNoRequest yesNoRequest = new YesNoRequest(context.DefendingUnit.PlayerID(), "Strike back?");

            Task<bool> task = GameContext.PlayerRequester
                .RequestDecision<YesNoRequest, bool>(yesNoRequest);

            await task;

            if(task.Result)
            {
                await MoveToStrikingBack(context);
            }
            else
            {
                await SkipStrikingBack(context);
            }
        }

        private Task MoveToStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders striking back.");
            return OnOfferAccepted.Activate(context);
        }

        private Task SkipStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders not striking back.");
            return OnOfferRejected.Activate(context);
        }
    }
}