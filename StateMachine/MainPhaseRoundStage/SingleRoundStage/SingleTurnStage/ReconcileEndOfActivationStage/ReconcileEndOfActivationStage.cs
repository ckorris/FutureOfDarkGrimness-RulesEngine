
namespace FDG.Stages
{

    public class ReconcileEndOfActivationStage : StageBase<ISingleTurnContext>
    {

        public StageBinding OnFinished;

        int _enterCount = 0;

        public ReconcileEndOfActivationStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent) : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            //Temp, just have it count to 3, as if there are three units to activate.
            _enterCount++;

            if (_enterCount < 3)
            {
                GameContext.Log($"ReconcileEndOfActivationStage entrance {_enterCount}");
                OnFinished.Activate(context);
            }
        }
    }
}