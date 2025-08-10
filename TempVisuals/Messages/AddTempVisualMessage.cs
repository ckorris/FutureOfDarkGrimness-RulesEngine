
namespace FDG.TempVisuals.Messages
{
    public record AddTempVisualMessage(TempVisual TempVisual);

    /* //If you uncommend, add using Newtonsoft.Json.
    public record AddTempVisualMessage
    {
        public readonly Guid ID;
        public readonly IMeshProvider Mesh;
        public readonly IMaterialProvider Material;
        public readonly Position Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        [JsonConstructor]
        public AddTempVisualMessage(Guid id, IMeshProvider mesh, IMaterialProvider material,
        Position position, Quaternion rotation, Vector3 scale)
        {
            ID = id;
            Mesh = mesh;
            Material = material;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public AddTempVisualMessage(ITempVisual tempVisual)
        {
            ID = tempVisual.ID;
            Mesh = tempVisual.Mesh;
            Material = tempVisual.Material;
            Position = tempVisual.Position;
            Rotation = tempVisual.Rotation;
            Scale = tempVisual.Scale;
        }
    }
    */
}
