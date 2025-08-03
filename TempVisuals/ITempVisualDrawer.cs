
namespace FDG.TempVisuals
{
    public interface ITempVisualDrawer
    {
        public void AddVisual(ITempVisual visual);

        public void UpdateVisual(ITempVisual visual);

        public void RemoveVisual(Guid tempVisualID);

        public void ClearAllVisuals();
    }
}
