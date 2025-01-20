
using FDG.Data;

namespace FDG.Stages
{

    public class DeploymentStage : StageBase<IGameContext>
    {
        //TODO: Will likely have to break into sub-stages in order to handle initial role, scout, ambush, and other things.

        public const string TO_MAIN_TRANSITION = "DeploymentToMain";

        public StageBinding ToMain;

        public DeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent) : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override void Enter(IGameContext context)
        {
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

            List<TeamData> allTeams = context.GameDataStore.GetAllValues<TeamData>().ToList();
            
            if(allTeams.Count() != 2)
            {
                throw new InvalidOperationException($"{nameof(DeploymentStage)} doesn't yet support team counts other than 2. " +
                    $"Number of teams provided: {allTeams.Count()}");
            }

            Dictionary<IPlayer, DeploymentTurnContext> turnContexts = new Dictionary<IPlayer, DeploymentTurnContext>();

            for(int i = 0; i < 2; i++)
            {
                TeamData teamData = allTeams[i];
                RectangularZone teamDeployZone = (i == 1) ? team1DeployZone : team2DeployZone;

                //TODO: Make not the interface? Refer to PlayerData directly, which would have to be exposed in TeamData.

                List<ArmyData> armies = context.GameDataStore.GetAllValues<ArmyData>().ToList();

                foreach (IPlayer player in teamData.Players)
                {
                    List<DataBinding<UnitData>> unitBindings = new List<DataBinding<UnitData>>();

                    foreach (ArmyData army in armies.Where(a => a.IsOwnedBy(player.ID)))
                    {

                        //foreach (DataReference reference in context.GameDataStore.GetAllDataReferences<UnitData>())
                        foreach(DataBinding<UnitData> dataBinding in army.UnitBindings)
                        {
                            unitBindings.Add(dataBinding);
                        }
                    }

                    DeploymentTurnContext deployTurnContext = new DeploymentTurnContext(unitBindings, teamDeployZone);

                    turnContexts.Add(player, deployTurnContext);
                }
            }

            IDeploymentHandler handler = GameContext.GetHandler<IDeploymentHandler>();
            DeployHandlerRepeater repeater = new DeployHandlerRepeater(turnContexts, handler, 
                () => ToMain.Activate(context), context);
        }

        private class DeployHandlerRepeater
        {
            //TODO: There's better ways to do this, but I'm running on very little sleep and just want this to work
            //so that I can test the net code better. Make some sort of generic tool for handling taking turns
            //within the scope of one stage.

            private Dictionary<IPlayer, DeploymentTurnContext> _contexts;

            //Temp, someday we'll have a player order, but for now, it's arbitrary.
            private List<IPlayer> _players;

            private int _playerIndex;

            private IDeploymentHandler _handler;

            private Action _onFinished;

            private IGameContext _gameContext;

            public DeployHandlerRepeater(Dictionary<IPlayer, DeploymentTurnContext> contexts, IDeploymentHandler handler,
                Action onFinished, IGameContext gameContext)
            {
                _players = contexts.Keys.ToList();
                _contexts = contexts;
                _playerIndex = 0;
                _handler = handler;
                _onFinished = onFinished;
                _gameContext = gameContext;

                IPlayer firstPlayer = _players[_playerIndex];
                DeploymentTurnContext nextTurnContext = _contexts[firstPlayer];

                _handler.Handle(nextTurnContext, OnChoiceMade);
            }


            private void OnChoiceMade(DeploymentSelection selection)
            {
                if(selection.Validate() == false)
                {
                    throw new ArgumentException("Submitted invalid deployment selection.");
                }

                //TODO: Actually move the models.
                foreach(KeyValuePair<DataBinding<ModelData>, Position> kvp in selection.ModelPositions)
                {
                    kvp.Key.GetValue().PositionBinding.SetValue(kvp.Value);
                }

                //If all the units have been placed from that turn context, remove it from the list.
                IPlayer lastPlayer = _players[_playerIndex];
                DeploymentTurnContext lastTurnContext = _contexts[lastPlayer];
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

                IPlayer nextPlayer = _players[_playerIndex];
                DeploymentTurnContext nextTurnContext = _contexts[nextPlayer];

                _handler.Handle(nextTurnContext, OnChoiceMade);
            }
        }
    }

    public interface IDeploymentHandler
    {
        void Handle(DeploymentTurnContext turnContext, Action<DeploymentSelection> onSelected);
    }
}