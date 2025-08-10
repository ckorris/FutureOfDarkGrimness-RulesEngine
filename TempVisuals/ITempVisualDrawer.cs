
using System.Drawing;
using System.Numerics;

namespace FDG.TempVisuals
{
    public interface ITempVisualDrawer
    {
        public void AddVisual(ITempVisual visual);

        public void UpdateVisualTransform(Guid tempVisualID, Position position, 
            Quaternion rotation, Vector3 scale);

        public void UpdateVisualColor(Guid tempVisualID, Color color);

        public void RemoveVisual(Guid tempVisualID);

        public void ClearAllVisuals();
    }
}
