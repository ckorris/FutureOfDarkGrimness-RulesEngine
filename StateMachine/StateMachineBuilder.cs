
namespace FDG.Stages
{
    public class StateMachineBuilder
    {
        private StateMachine _stateMachine = new StateMachine();
        private ITopLevelContext _topLevelContext;
        private IMainPhaseContext _mainPhaseContext;
        private IPlayerTurnContext _playerTurnContext;
        private IUnitActionContext _unitActionContext;
        private IMeleeContext _meleeContext;
        private IRangedContext _rangedContext;

        public StateMachineBuilder(ITopLevelContext topLevelContext, IMainPhaseContext mainPhaseContext,
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
            ArmySetupStage armySetupState = new ArmySetupStage(_stateMachine, _topLevelContext);
            MapSetupStage mapSetupState = new MapSetupStage(_stateMachine, _topLevelContext);
            DeploymentStage deploymentState = new DeploymentStage(_stateMachine, _topLevelContext);
            MainPhaseRoundStage mainPhaseRoundState = new MainPhaseRoundStage(_stateMachine, _topLevelContext,
                _mainPhaseContext, _playerTurnContext, _unitActionContext, _meleeContext, _rangedContext);
            VictoryCalculationStage victoryCalculationState = new VictoryCalculationStage(_stateMachine, _topLevelContext);

            mainPhaseRoundState.AssignExitStage(victoryCalculationState);

            _stateMachine.AddTransition<ArmySetupStage>(ArmySetupStage.TO_MAP_SETUP_TRANSITION, mapSetupState);
            _stateMachine.AddTransition<MapSetupStage>(MapSetupStage.TO_DEPLOYMENT_TRANSITION, deploymentState);
            _stateMachine.AddTransition<DeploymentStage>(DeploymentStage.TO_MAIN_TRANSITION, mainPhaseRoundState);            
            
            _stateMachine.Start(armySetupState);

            return _stateMachine;
        }
    }
}