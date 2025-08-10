using System.Drawing;

namespace FDG.TempVisuals.Messages
{
    public record UpdateTempVisualColorMessage(Guid TempVisualID, Color Color);
}
