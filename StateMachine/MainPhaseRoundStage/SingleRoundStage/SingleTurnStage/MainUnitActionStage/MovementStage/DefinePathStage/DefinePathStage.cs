using System;
using System.Text;
using FDG.StageResolution.Requests;

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

            float hardCap = System.Math.Max(context.MaxRushDistance, context.MaxChargeDistance);

            var pathRequest = new DefineMovementPathRequest(playerID, "Move Unit", context.MovingUnit,
                context.MaxAdvanceDistance, context.MaxRushDistance, hardCap);

            List<ModelMoveEntry> movements = await context.PlayerRequester()
                .RequestDecision<DefineMovementPathRequest, List<ModelMoveEntry>>(pathRequest);

            List<Position> enemyPositions = MovementUtilities.GetEnemyModelPositions(context.MovingUnit, context.GameContext);

            if(MovementUtilities.ValidatePaths(movements, context.MaxRushDistance, hardCap,
                enemyPositions,
                context.RelevantTerrain, out List<ReasonForInvalidMove> invalidReasons) == false)
            {
                StringBuilder sb = new StringBuilder(invalidReasons[0].ToString());
                for(int i = 1; i < invalidReasons.Count; i++)
                {
                    sb.Append(", " + invalidReasons[i].ToString());
                }

                throw new RequestResponseInvalidException($"Response to {nameof(DefinePathStage)} movement request was invalid for the following reasons: "
                    + sb.ToString());
            }

            context.SubmitValidPathTemplate(movements);

            OnPathDefined.Activate(context);
        }

    }
}
