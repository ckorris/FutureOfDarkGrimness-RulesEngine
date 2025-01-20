
using FDG.Data;

namespace FDG.Stages
{
    public class DeploymentTurnContext
    {
        public List<IUnit> RemainingUnits
        {
            get
            {
                return _unitBindings.Select(binding => binding.GetValue() as IUnit).ToList();
            }
        }

        public readonly RectangularZone DeploymentZone;

        private List<DataBinding<UnitData>> _unitBindings;

        public DeploymentTurnContext(List<DataBinding<UnitData>> remainingUnits, RectangularZone deploymentZone)
        {
            if(remainingUnits.Count == 0)
            {
                throw new InvalidOperationException($"Tried to make a {nameof(DeploymentTurnContext)} " +
                    "with an empty unit list.");
            }

            _unitBindings = remainingUnits;
            DeploymentZone = deploymentZone;
        }

        public DeploymentSelection GetNewSelection(IUnit selectedUnit)
        {
            if(RemainingUnits.Contains(selectedUnit) == false)
            {
                throw new ArgumentException($"Selected unit {selectedUnit.Name} not in {nameof(DeploymentTurnContext)}.");
            }

            //RemainingUnits.Remove(selectedUnit); //TODO: How does this compile? Does it work?

            DataBinding<UnitData> chosenUnitBinding = _unitBindings.First(unit => unit.GetValue() == selectedUnit);

            _unitBindings.Remove(chosenUnitBinding);

            return new DeploymentSelection(chosenUnitBinding.GetValue(), DeploymentZone);
        }
    }

}
