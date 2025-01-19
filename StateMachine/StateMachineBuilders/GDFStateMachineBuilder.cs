using FDG;
using FDG.Stages;
using FDG.Stages.Builders;

namespace FutureOfDarkGrimness.StateMachine.StateMachineBuilders
{
    public class GDFStateMachineBuilder : IStateMachineBuilder<IGameContext>
    {
        public Dictionary<string, StageBase<IGameContext>> BuildStateMachine(StateMachine<IGameContext> stateMachine, IGameContext gameContext,
            out StageBase<IGameContext> startingStage)
        {
            PlayerSetupStage playerSetupStage = new PlayerSetupStage(gameContext, stateMachine);
            ArmySetupStage armySetupStage = new ArmySetupStage(gameContext, stateMachine);
            MapSetupStage mapSetupStage = new MapSetupStage(gameContext, stateMachine);
            DeploymentStage deploymentStage = new DeploymentStage(gameContext, stateMachine);
            MainPhaseRoundStage mainPhaseRoundStage = new MainPhaseRoundStage(gameContext, stateMachine);
            VictoryCalculationStage victoryCalculationStage = new VictoryCalculationStage(gameContext, stateMachine);

            playerSetupStage.ToArmySetup.Bind(armySetupStage);
            armySetupStage.ToMapSetup.Bind(mapSetupStage);
            mapSetupStage.ToDeployment.Bind(deploymentStage);
            deploymentStage.ToMain.Bind(mainPhaseRoundStage);
            mainPhaseRoundStage.ToVictoryCalculation.Bind(victoryCalculationStage);

            //TODO: This is awkward but it's meant to match how parent bindings works everywhere else, where it makes more sense.
            Dictionary<string, StageBase<IGameContext>> bindings = new Dictionary<string, StageBase<IGameContext>>()
            {
                {armySetupStage.Name, armySetupStage},
                {mapSetupStage.Name, mapSetupStage},
                {deploymentStage.Name, deploymentStage},
                {mainPhaseRoundStage.Name, mainPhaseRoundStage},
                {victoryCalculationStage.Name, victoryCalculationStage}
            };

            startingStage = playerSetupStage;

            return bindings;
        }
    }
}
