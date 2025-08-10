using System.Numerics;

namespace FDG.TempVisuals.Messages
{
    public record UpdateTempVisualTransformMessage(Guid VisualID, Position Position, 
        Quaternion Rotation, Vector3 Scale);
}
