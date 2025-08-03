using FDG.SerializableVisuals;
using System.Numerics;

namespace FDG.TempVisuals
{
    public interface ITempVisual
    {
        Guid ID { get; }

        IMeshProvider Mesh { get; }

        IMaterialProvider Material { get; }

        Position Position { get; }

        Quaternion Rotation { get; }

        Vector3 Scale { get; }

    }
}
