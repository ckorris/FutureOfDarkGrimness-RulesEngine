
namespace FDG.Stages
{
    public class DeploymentSelection
    {
        public readonly IUnit SelectedUnit;

        public readonly RectangularZone DeploymentZone;

        public Dictionary<IModel, Position> ModelPositions = new Dictionary<IModel, Position>();

        internal DeploymentSelection(IUnit selectedUnit, RectangularZone deploymentZone)
        {
            SelectedUnit = selectedUnit;
            DeploymentZone = deploymentZone;
        }

        public void SetModelPosition(IModel model, Position position)
        {
            if (SelectedUnit.Models.Contains(model) == false)
            {
                throw new ArgumentException($"Assigned position for model that wasn't in the chosen unit.");
            }

            if (ValidatePosition(model, model.Position) == false)
            {
                throw new ArgumentException($"Tried to place model in invalid position. Use ValidatePosition method to check.");
            }

            ModelPositions[model] = position;
        }

        public bool Validate()
        {
            foreach (IModel model in SelectedUnit.Models)
            {
                if (ModelPositions.ContainsKey(model) == false)
                {
                    return false;
                }
            }

            return true;
        }

        public bool ValidatePosition(IModel model, Position position)
        {
            //TODO: Using some kind of input parameters, make sure a position is valid.
            //Also make sure the models aren't overlapping.
            return true;
        }
    }
}
