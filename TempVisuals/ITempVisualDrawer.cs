
using System.Numerics;
using System.Security.AccessControl;

namespace FDG.TempVisuals
{
    public interface ITempVisualDrawer
    {
        public void AddVisual(ITempVisual visual);

        public void UpdateVisual(ITempVisual visual);

        public void UpdateVisualTransform(Guid tempVisualID, Position position, 
            Quaternion rotation, Vector3 scale);

        public void RemoveVisual(Guid tempVisualID);

        public void ClearAllVisuals();
    }
}
