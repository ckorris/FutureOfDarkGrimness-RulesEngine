
using FDG.Data;

namespace FDG.Stages
{

    public class DeploymentStage : ParentStage<IGameContext, IDeploymentContext>
    {
        //TODO: Will likely have to break into sub-stages in order to handle initial role, scout, ambush, and other things.

        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public StageBinding? ToMain;

        public DeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            GameContext.TextOutput.Log($"Deployment stage enter.");

            base.Enter(context);
        }

        protected override IDeploymentContext GetNewChildContext(IGameContext contextSelf)
        {
            return new DeploymentContext(GameContext);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IDeploymentContext> startingChild)
        {
            ToMain = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new RollForMapSideStage(GameContext, this), out var rollForMapSide)
                .AddChild(new ChooseMapSideStage(GameContext, this), out var chooseMapSide)
                .AddChild(new RollForFirstDeploymentStage(GameContext, this), out var rollForFirstDeployment)
                .AddChild(new DeployAllUnitsStage(GameContext, this), out var deployAllUnits)
                .AddSibling(nameof(ToMain), ToMain, out string toMainEvent)
                .Build();

            startingChild = rollForMapSide;

            rollForMapSide.ToChooseMapSide.Bind(chooseMapSide);
            chooseMapSide.ToRollForFirstDeployment.Bind(rollForFirstDeployment);
            rollForFirstDeployment.ToDeployAllUnits.Bind(deployAllUnits);
            deployAllUnits.ToMain.Bind(toMainEvent);

            return dictionary;
        }
    }
}