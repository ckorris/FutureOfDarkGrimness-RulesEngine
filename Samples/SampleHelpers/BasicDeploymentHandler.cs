using FDG.Stages;
using System.Reflection.Metadata;


namespace FDG.Samples
{
    public class BasicDeploymentHandler : IDeploymentHandler
    {
        private float _xValue = 0;

        private const float LEFT_PADDING_INCHES = 4;
        private const float UNIT_PADDING_INCHES = 6;

        public void Handle(DeploymentTurnContext turnContext, Action<DeploymentSelection> onSelected)
        {
            //Just line up the next unit's models along the middle of the zone.
            RectangularZone deployZone = turnContext.DeploymentZone;

            float yValue = deployZone.Bottom + (deployZone.Top - deployZone.Bottom) / 2;

            IUnit unit = turnContext.RemainingUnits[0];

            DeploymentSelection selection = turnContext.GetNewSelection(unit);

            foreach(IModel model in unit.Models)
            {
                Float2 deployPoint = new Float2(LEFT_PADDING_INCHES + _xValue, yValue);
                _xValue++;
                selection.SetModelPosition(model, new Position(deployPoint));
            }

            _xValue += UNIT_PADDING_INCHES;

            onSelected?.Invoke(selection);
        }
    }
}
