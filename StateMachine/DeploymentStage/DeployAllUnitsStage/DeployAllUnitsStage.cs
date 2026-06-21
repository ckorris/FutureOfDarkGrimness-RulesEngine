
using FDG.Data;

namespace FDG.Stages
{
    public class DeployAllUnitsStage : ParentStage<IDeploymentContext, IDeploymentTurnContext>
    {
        public StageBinding ToMain;

        public DeployAllUnitsStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent) { }

        protected override IDeploymentTurnContext GetNewChildContext(IDeploymentContext contextSelf)
        {
            if(contextSelf.FirstDeploymentRollOrder == null)
            {
                throw new NullReferenceException($"{nameof(contextSelf.FirstDeploymentRollOrder)}");
            }

            if (contextSelf.PlayerDeploymentZones == null)
            {
                throw new NullReferenceException($"{nameof(contextSelf.PlayerDeploymentZones)}");
            }

            return new DeploymentTurnContext(GameContext, contextSelf.FirstDeploymentRollOrder, contextSelf.PlayerDeploymentZones);
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IDeploymentTurnContext> startingChild)
        {
            ToMain = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineNextDeployPlayerStage(GameContext, this), out var determineNextDeployPlayer)
                .AddChild(new ChooseUnitToDeployStage(GameContext, this), out var chooseUnitToDeploy)
                .AddChild(new ChooseDeployActionStage(GameContext, this), out var chooseDeployAction)
                .AddChild(new DeployUnitStage(GameContext, this), out var deployUnitStage)
                .AddChild(new PlaceDeferredUnitsStage(GameContext, this), out var placeDeferredUnits)
                .AddSibling(nameof(ToMain), ToMain, out string toMainEvent)
                .Build();

            startingChild = determineNextDeployPlayer;

            determineNextDeployPlayer.OnFinish.Bind(chooseUnitToDeploy);
            // Normal deployment done → place the units a rule set aside (Scout), then exit to main.
            determineNextDeployPlayer.OnFinishedDeployingAllUnits.Bind(placeDeferredUnits);
            chooseUnitToDeploy.OnFinish.Bind(chooseDeployAction);
            // Holding a unit in Ambush counts as that player's deployment for the turn — no placement,
            // straight back to pick the next player.
            chooseUnitToDeploy.OnDeferred.Bind(determineNextDeployPlayer);
            chooseDeployAction.OnFinish.Bind(deployUnitStage);
            // Loading a unit into a transport (#035) likewise counts as the turn with no placement —
            // skip DeployUnit and go back to pick the next player.
            chooseDeployAction.OnEmbarked.Bind(determineNextDeployPlayer);
            deployUnitStage.OnFinish.Bind(determineNextDeployPlayer);
            placeDeferredUnits.OnFinish.Bind(toMainEvent);

            return dictionary;
        }
    }
}

