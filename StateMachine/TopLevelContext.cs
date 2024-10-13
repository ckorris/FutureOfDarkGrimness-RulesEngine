using System;

namespace FDG.StateMachine
{
    public interface ITopLevelContext : ICommonContextItems
    {
        public IArmySetupHandler ArmySetupHandler { get; }

        public IMapSetupHandler MapSetupHandler { get; }

        public IDeploymentHandler DeploymentHandler { get; }
    }

    public class TopLevelContext : ITopLevelContext
    {
        public IArmySetupHandler ArmySetupHandler { get; private set; }

        public IMapSetupHandler MapSetupHandler { get; private set; }

        public IDeploymentHandler DeploymentHandler { get; private set; }

        public ITextOutput TextOutput { get; private set; }

        public IDiceRoller DiceRoller { get; private set; }

        public TopLevelContext(IArmySetupHandler armySetupHandler, IMapSetupHandler mapSetupHandler, 
            IDeploymentHandler deploymentHandler, ITextOutput textOutput, IDiceRoller diceRoller)
        {
            ArmySetupHandler = armySetupHandler;
            MapSetupHandler = mapSetupHandler;
            DeploymentHandler = deploymentHandler;
            TextOutput = textOutput;
            DiceRoller = diceRoller;
        }
    }
}