
namespace FDG.Stages
{
    public class StateMachineBuilder
    {
        private StateMachine _stateMachine = new StateMachine();
        private IGameContext _topLevelContext;
        private IMainPhaseContext _mainPhaseContext;
        private IPlayerTurnContext _playerTurnContext;
        private IUnitActionContext _unitActionContext;
        private IMeleeContext _meleeContext;
        private IRangedContext _rangedContext;

        public StateMachineBuilder(IGameContext topLevelContext, IMainPhaseContext mainPhaseContext,
            IPlayerTurnContext playerTurnContext, IUnitActionContext unitActionContext, 
            IMeleeContext meleeContext, IRangedContext rangedContext)
        {
            _topLevelContext = topLevelContext;
            _mainPhaseContext = mainPhaseContext;
            _playerTurnContext = playerTurnContext;
            _unitActionContext = unitActionContext;
            _meleeContext = meleeContext;
            _rangedContext = rangedContext;
        }

        public StateMachine Build()
        {
            //Top level stages.
            ArmySetupStage armySetupStage = new ArmySetupStage(_stateMachine, _topLevelContext);
            MapSetupStage mapSetupStage = new MapSetupStage(_stateMachine, _topLevelContext);
            DeploymentStage deploymentStage = new DeploymentStage(_stateMachine, _topLevelContext);
            MainPhaseRoundStage mainPhaseRoundStage = new MainPhaseRoundStage(_stateMachine, _topLevelContext,
                _mainPhaseContext, _playerTurnContext, _unitActionContext, _meleeContext, _rangedContext);
            VictoryCalculationStage victoryCalculationStage = new VictoryCalculationStage(_stateMachine, _topLevelContext);

            mainPhaseRoundStage.AssignExitStage(victoryCalculationStage);

            armySetupStage.Bind(ArmySetupStage.TO_MAP_SETUP_TRANSITION, mapSetupStage);
            mapSetupStage.Bind(MapSetupStage.TO_DEPLOYMENT_TRANSITION, deploymentStage);
            deploymentStage.Bind(DeploymentStage.TO_MAIN_TRANSITION, mainPhaseRoundStage);            
            
            _stateMachine.Start(armySetupStage);

            return _stateMachine;
        }
    }
}