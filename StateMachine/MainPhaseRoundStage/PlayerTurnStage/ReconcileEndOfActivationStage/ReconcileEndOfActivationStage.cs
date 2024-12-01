
namespace FDG.Stages
{

    public class ReconcileEndOfActivationStage : StageBase<IPlayerTurnContext>
    {

        public StageBinding ToDeterminePlayerTurn;
        public StageBinding ToReconcileObjectives;

        int _enterCount = 0;

        public ReconcileEndOfActivationStage(IGameContext gameContext, IStateMachineLayer<IPlayerTurnContext> parent) : base(gameContext, parent)
        {
            ToDeterminePlayerTurn = new StageBinding(this);
            ToReconcileObjectives = new StageBinding(this);
        }

        public override void Enter(IPlayerTurnContext context)
        {
            //Temp, just have it count to 3, as if there are three units to activate.
            _enterCount++;

            if (_enterCount < 3)
            {
                GameContext.Log($"ReconcileEndOfActivationStage entrance {_enterCount}. Restarting turn.");
                ToDeterminePlayerTurn.Activate(context);
            }
            else
            {
                GameContext.Log("ReconcileEndOfActivationStage entrance 3. Ending round, moving to reconcile objectives.");
                _enterCount = 0;
                ToReconcileObjectives.Activate(context);
            }
        }
    }
}