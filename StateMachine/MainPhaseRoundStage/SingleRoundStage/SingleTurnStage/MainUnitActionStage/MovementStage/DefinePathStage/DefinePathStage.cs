
using FDG.StageResolution.Requests;
using System;

namespace FDG.Stages
{
    public class DefinePathStage : StageBase<IMovementActionContext>
    {
        public StageBinding OnPathDefined;

        public DefinePathStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
            OnPathDefined = new StageBinding(this);
        }

        public override async Task Enter(IMovementActionContext context)
        {

            PlayerID playerID = context.MovingUnit.GetValue().PlayerID; //Shorthand.

            var pathRequest = new DefineMovementPathRequest(playerID, "Move Unit", context.MovingUnit);

            List<ModelMoveEntry> movements = await context.PlayerRequester()
                .RequestDecision<DefineMovementPathRequest, List<ModelMoveEntry>>(playerID, pathRequest);

            /*
            IDefinePathHandler pathHandler = GameContext.GetHandler<IDefinePathHandler>();
            pathHandler.Handle(pathTemplate, () => OnSubmittedTemplateAsValid(pathTemplate, context));
            */
        }

        private void OnSubmittedTemplateAsValid(PathTemplate pathTemplate, IMovementActionContext context)
        {
            context.SubmitValidPathTemplate(pathTemplate);

            OnPathDefined.Activate(context);
        }
    }

    public interface IDefinePathHandler
    {
        public void Handle(PathTemplate pathTemplate, Action onTemplateValid); //TODO: Will need a lot more info.
    }
}
