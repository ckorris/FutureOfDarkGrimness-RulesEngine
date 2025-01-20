using FDG.Stages;
using System.Reflection.Metadata;


namespace FDG.Samples
{
    public class BasicDeploymentHandler : IDeploymentHandler
    {
        private const float LEFT_PADDING_INCHES = 4;
        private const float UNIT_PADDING_INCHES = 4;

        private Dictionary<RectangularZone, float> _xValuesPerZone = new Dictionary<RectangularZone, float>();

        public void Handle(DeploymentTurnContext turnContext, Action<DeploymentSelection> onSelected)
        {
            //Just line up the next unit's models along the middle of the zone.
            RectangularZone deployZone = turnContext.DeploymentZone;
            if(_xValuesPerZone.ContainsKey(deployZone) == false)
            {
                _xValuesPerZone[deployZone] = 0;
            }

            float yValue = deployZone.Bottom + (deployZone.Top - deployZone.Bottom) / 2;

            IUnit unit = turnContext.RemainingUnits[0];

            DeploymentSelection selection = turnContext.GetNewSelection(unit);

            foreach(IModel model in unit.Models)
            {
                Float2 deployPoint = new Float2(LEFT_PADDING_INCHES + _xValuesPerZone[deployZone], yValue);
                _xValuesPerZone[deployZone]++;
                selection.SetModelPosition(model, new Position(deployPoint));
            }

            _xValuesPerZone[deployZone] += UNIT_PADDING_INCHES;

            onSelected?.Invoke(selection);
        }
    }
}
