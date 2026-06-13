using FDG;
using FDG.Stages;

namespace FDG.StateMachine.StateMachineBuilders
{
    public class GDFStateMachineBuilder : IStateMachineBuilder<IGameContext>
    {
        public Dictionary<string, StageBase<IGameContext>> BuildStateMachine(StateMachine<IGameContext> stateMachine, IGameContext gameContext,
            out StageBase<IGameContext> startingStage)
        {
            MapSetupStage mapSetupStage = new MapSetupStage(gameContext, stateMachine);
            DeploymentStage deploymentStage = new DeploymentStage(gameContext, stateMachine);
            MainPhaseRoundStage mainPhaseRoundStage = new MainPhaseRoundStage(gameContext, stateMachine);
            VictoryCalculationStage victoryCalculationStage = new VictoryCalculationStage(gameContext, stateMachine);

            mapSetupStage.ToDeployment.Bind(deploymentStage);
            deploymentStage.ToMain.Bind(mainPhaseRoundStage);
            mainPhaseRoundStage.ToVictoryCalculation.Bind(victoryCalculationStage);

            //TODO: This is awkward but it's meant to match how parent bindings works everywhere else, where it makes more sense.
            Dictionary<string, StageBase<IGameContext>> bindings = new Dictionary<string, StageBase<IGameContext>>()
            {
                {mapSetupStage.Name, mapSetupStage},
                {deploymentStage.Name, deploymentStage},
                {mainPhaseRoundStage.Name, mainPhaseRoundStage},
                {victoryCalculationStage.Name, victoryCalculationStage}
            };

            startingStage = mapSetupStage;

            return bindings;
        }
    }
}
