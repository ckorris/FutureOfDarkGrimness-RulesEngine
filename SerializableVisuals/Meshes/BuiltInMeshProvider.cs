using FDG.BuiltInAssets;
using System.Numerics;
using System.Text;

namespace FDG.SerializableVisuals.Meshes
{
    public class BuiltInMeshProvider : IMeshProvider
    {
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Vector2> uvs = new List<Vector2>();
        private List<Vector3> normals = new List<Vector3>();

        public Vector3[] Vertices => _vertices;

        public int[] Triangles => _triangles;

        public Vector2[] UVs => _uvs;

        public Vector3[] Normals => _normals;

        private readonly Vector3[] _vertices;

        private readonly int[] _triangles;

        private readonly Vector2[] _uvs;

        private readonly Vector3[] _normals;

        public BuiltInMeshProvider(string resourcePath)
        {
            byte[] meshData = BuiltInAssetHelper.GetEmbeddedResource(resourcePath);

            string objData = Encoding.UTF8.GetString(meshData);
            StringReader reader = new StringReader(objData);

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();

            string line;

            while((line = reader.ReadLine()) != null)
            {
                if(line.StartsWith("v ")) //Vertex.
                {
                    string[]? parts = line.Substring(2).Split(' ');
                    vertices.Add(new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2])));
                }
                else if (line.StartsWith("f ")) //Face.
                {
                    string[]? parts = line.Substring(2).Split(' ');
                    foreach (string part in parts)
                    {
                        //OBJ format indices are 1-based, so subtract 1 to get 0-based indices.
                        int index = int.Parse(part.Split('/')[0]) - 1;
                        triangles.Add(index);
                    }
                }
                else if(line.StartsWith("vt ")) //UVs.
                {
                    string[]? parts = line.Substring(3).Split(' ');
                    uvs.Add(new Vector2(float.Parse(parts[0]), float.Parse(parts[1])));
                }
                else if (line.StartsWith("vn ")) //Vertex normals.
                {
                    string[]? parts = line.Substring(3).Split(' ');
                    normals.Add(new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2])));
                }
            }

            _vertices = vertices.ToArray();
            _triangles = triangles.ToArray();
            _uvs = uvs.ToArray();
            _normals = normals.ToArray();
        }


    }
}
