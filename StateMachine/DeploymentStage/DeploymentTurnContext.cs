
namespace FDG.Stages
{
    public class DeploymentTurnContext
    {
        public readonly List<IUnit> RemainingUnits;

        public readonly RectangularZone DeploymentZone;

        public DeploymentTurnContext(List<IUnit> remainingUnits, RectangularZone deploymentZone)
        {
            if(remainingUnits.Count == 0)
            {
                throw new InvalidOperationException($"Tried to make a {nameof(DeploymentTurnContext)} " +
                    "with an empty unit list.");
            }

            RemainingUnits = remainingUnits;
            DeploymentZone = deploymentZone;
        }

        public DeploymentSelection GetNewSelection(IUnit selectedUnit)
        {
            if(RemainingUnits.Contains(selectedUnit) == false)
            {
                throw new ArgumentException($"Selected unit {selectedUnit.Name} not in {nameof(DeploymentTurnContext)}.");
            }

            RemainingUnits.Remove(selectedUnit);

            return new DeploymentSelection(selectedUnit, DeploymentZone);
        }
    }

}
