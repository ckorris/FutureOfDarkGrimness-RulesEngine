using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

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
            // #203: stage transitions complete synchronously, so without a yield every decision in the
            // game leaves frames on one ever-deepening call stack and a long game dies as an uncatchable
            // StackOverflow. Yielding once per activation unwinds the accumulated chain, bounding stack
            // depth by the deepest single activation instead of the whole game.
            await Task.Yield();

            GameContext.LogDebug($"ReconcileEndOfActivationStage entrance {_enterCount}");

            // Clear the just-activated unit's "used this activation" markers (once-per-activation cost gates,
            // e.g. Strafing) so they reset for its next activation.
            if (context.ActivatedUnit != null)
            {
                IUnit unit = context.ActivatedUnit.GetValue();
                List<ITokenContainer> containers = new List<ITokenContainer> { unit.Tokens };
                containers.AddRange(unit.Models.Select(model => model.Tokens));
                new TokenClearService().ClearForHook(EHookID.Activation_OnEndOfActivation, containers);
            }

            // Activation over -- clear the spotlight target so nothing is highlighted between activations.
            if (GameContext.GameDataStore.IsTypeAssigned<GameProgressData>())
                GameProgressUtilities.SetActivatingUnit(GameContext.GameDataStore, null);

            await OnFinished.Activate(context);
        }
    }
}
