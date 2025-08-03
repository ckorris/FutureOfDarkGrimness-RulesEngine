using FDG.SerializableVisuals;
using Newtonsoft.Json;
using System.Numerics;

namespace FDG.TempVisuals
{
    [Serializable]
    public class TempVisual : ITempVisual
    {
        public Guid ID { get; private set; }

        public IMeshProvider Mesh { get; private set; }

        public IMaterialProvider Material { get; private set; }

        public Position Position { get; private set; }

        public Quaternion Rotation { get; private set; }

        public Vector3 Scale { get; private set; }
        

        public TempVisual(IMeshProvider meshProvider, IMaterialProvider materialProvider,
            Position position, Quaternion rotation = default, Vector3 scale = default)
        {
            ID = Guid.NewGuid();

            //Freaking Identity and One not being compile time constants.
            if(rotation == default)
            {
                rotation = Quaternion.Identity;
            }

            if(scale == default)
            {
                scale = Vector3.One;
            }

            Mesh = meshProvider;
            Material = materialProvider;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        [JsonConstructor]
        public TempVisual(Guid id, IMeshProvider mesh, IMaterialProvider material,
            Position position, Quaternion rotation, Vector3 scale)
        {
            ID = id;
            Mesh = mesh;
            Material = material;
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }
}
