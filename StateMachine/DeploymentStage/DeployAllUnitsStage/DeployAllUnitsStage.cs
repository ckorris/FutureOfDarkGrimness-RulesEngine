
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
                .AddSibling(nameof(ToMain), ToMain, out string toMainEvent)
                .Build();

            startingChild = determineNextDeployPlayer;

            determineNextDeployPlayer.OnFinish.Bind(chooseUnitToDeploy);
            determineNextDeployPlayer.OnFinishedDeployingAllUnits.Bind(toMainEvent);
            chooseUnitToDeploy.OnFinish.Bind(chooseDeployAction);
            chooseDeployAction.OnFinish.Bind(deployUnitStage);
            deployUnitStage.OnFinish.Bind(chooseUnitToDeploy);

            return dictionary;
        }

        /*
        public override Task Enter(IDeploymentContext context)
        {
            //Below was copy-pasted from parent.
            context.Log($"Entered {nameof(DeploymentStage)}.");
            //GameContext.GetHandler<IDeploymentHandler>().Handle(GameContext, ToMain.Activate);
            //TODO: Make list of all choices, put in some kind of context object, then repeat handler call
            //until all things have been positioned.
            //Also needs a way to validate valid placement.

            RectangularZone team1DeployZone = new RectangularZone(
                0,
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                0,
                GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);

            RectangularZone team2DeployZone = new RectangularZone(
                0,
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

            List<TeamData> allTeams = context.GameContext.GameDataStore.GetAllValues<TeamData>().ToList();

            if (allTeams.Count() != 2)
            {
                throw new InvalidOperationException($"{nameof(DeploymentStage)} doesn't yet support team counts other than 2. " +
                    $"Number of teams provided: {allTeams.Count()}");
            }

            Dictionary<PlayerID, DeploymentTurn> turnContexts = new Dictionary<PlayerID, DeploymentTurn>();

            for (int i = 0; i < 2; i++)
            {
                TeamData teamData = allTeams[i];
                RectangularZone teamDeployZone = (i == 1) ? team1DeployZone : team2DeployZone;

                //TODO: Make not the interface? Refer to PlayerData directly, which would have to be exposed in TeamData.

                List<ArmyData> armies = context.GameDataStore().GetAllValues<ArmyData>().ToList();

                foreach (PlayerID player in teamData.Players)
                {
                    List<DataBinding<UnitData>> unitBindings = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(player)))
                    {

                        //foreach (DataReference reference in context.GameDataStore.GetAllDataReferences<UnitData>())
                        foreach (DataBinding<UnitData> dataBinding in army.UnitBindings)
                        {
                            unitBindings.Add(dataBinding);
                        }
                    }

                    DeploymentTurn deployTurnContext = new DeploymentTurn(unitBindings, teamDeployZone);

                    turnContexts.Add(player, deployTurnContext);
                }
            }

            throw new NotImplementedException();

            
        }

   
        private class DeployHandlerRepeater
        {
            //TODO: There's better ways to do this, but I'm running on very little sleep and just want this to work
            //so that I can test the net code better. Make some sort of generic tool for handling taking turns
            //within the scope of one stage.

            private Dictionary<PlayerID, DeploymentTurn> _contexts;

            //Temp, someday we'll have a player order, but for now, it's arbitrary.
            private List<PlayerID> _players;

            private int _playerIndex;

            private IDeploymentHandler _handler;

            private Action _onFinished;

            private IGameContext _gameContext;

            public DeployHandlerRepeater(Dictionary<PlayerID, DeploymentTurn> contexts, IDeploymentHandler handler,
                Action onFinished, IGameContext gameContext)
            {
                _players = contexts.Keys.ToList();
                _contexts = contexts;
                _playerIndex = 0;
                _handler = handler;
                _onFinished = onFinished;
                _gameContext = gameContext;

                PlayerID firstPlayer = _players[_playerIndex];
                DeploymentTurn nextTurnContext = _contexts[firstPlayer];

                _handler.Handle(nextTurnContext, OnChoiceMade);
            }


            private void OnChoiceMade(DeploymentSelection selection)
            {
                if (selection.Validate() == false)
                {
                    throw new ArgumentException("Submitted invalid deployment selection.");
                }

                //TODO: Actually move the models.
                foreach (KeyValuePair<DataBinding<ModelData>, Position> kvp in selection.ModelPositions)
                {
                    kvp.Key.GetValue().PositionBinding.SetValue(kvp.Value);
                }

                //If all the units have been placed from that turn context, remove it from the list.
                PlayerID lastPlayer = _players[_playerIndex];
                DeploymentTurn lastTurnContext = _contexts[lastPlayer];
                if (lastTurnContext.RemainingUnits.Count == 0)
                {
                    _contexts.Remove(lastPlayer);
                    _players.Remove(lastPlayer);
                }
                else
                {
                    _playerIndex++;
                }

                if (_contexts.Count == 0)
                {
                    _onFinished();
                    return;
                }

                if (_playerIndex >= _players.Count)
                {
                    _playerIndex = 0;
                }

                PlayerID nextPlayer = _players[_playerIndex];
                DeploymentTurn nextTurnContext = _contexts[nextPlayer];

                _handler.Handle(nextTurnContext, OnChoiceMade);
            }
        }
        */
    }

    /*
    public interface IDeploymentHandler
    {
        void Handle(DeploymentTurn turnContext, Action<DeploymentSelection> onSelected);
    }
    */
}

