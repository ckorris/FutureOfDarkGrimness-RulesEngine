
using FDG.Data;
using System.Diagnostics;

namespace FDG.Stages
{
    public class DeploymentSelection
    {
        public IUnit SelectedUnit => _selectedUnit;

        public readonly RectangularZone DeploymentZone;

        internal Dictionary<DataBinding<ModelData>, Position> ModelPositions 
            = new Dictionary<DataBinding<ModelData>, Position>();

        private UnitData _selectedUnit;

        internal DeploymentSelection(UnitData selectedUnit, RectangularZone deploymentZone)
        {
            _selectedUnit = selectedUnit;
            DeploymentZone = deploymentZone;
        }

        public void SetModelPosition(IModel model, Position position)
        {
            Debug.WriteLine($"Set model position to {position.x}, {position.z}");

            if (SelectedUnit.Models.Contains(model) == false)
            {
                throw new ArgumentException($"Assigned position for model that wasn't in the chosen unit.");
            }

            if (ValidatePosition(model, position) == false)
            {
                throw new ArgumentException($"Tried to place model in invalid position: {position.x}, {position.z}. " + 
                    "Use ValidatePosition method to check.");
            }

            DataBinding<ModelData> modelData = _selectedUnit.ModelBindings.First(binding => binding.GetValue() == model);

            ModelPositions[modelData] = position;
        }

        public bool Validate()
        {
            foreach (DataBinding<ModelData> modelData in _selectedUnit.ModelBindings)
            {
                if (ModelPositions.ContainsKey(modelData) == false)
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
            return DeploymentZone.IsPointWithinZone(position);
        }
    }
}
