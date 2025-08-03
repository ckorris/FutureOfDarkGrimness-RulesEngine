using System.Drawing;
using System.Numerics;

namespace FDG.SerializableVisuals
{
    public interface IMeshProvider
    {
        public Vector3[] Vertices { get; }

        public int[] Triangles { get; }

        public Vector2[] UVs { get; }

        public Vector3[] Normals { get; }
    }
}
