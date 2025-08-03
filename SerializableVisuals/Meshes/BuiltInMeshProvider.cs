using FDG.BuiltInAssets;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;

namespace FDG.SerializableVisuals.Meshes
{
    [Serializable]
    public class BuiltInMeshProvider : IMeshProvider
    {
        public readonly string ResourcePath;

        [JsonIgnore]
        public Vector3[] Vertices
        {
            get
            {
                LoadIfNeeded();
                return _vertices;
            }
        }

        [JsonIgnore]
        public int[] Triangles
        {
            get
            {
                LoadIfNeeded();
                return _triangles;
            }
        }

        [JsonIgnore]
        public Vector2[] UVs
        {
            get
            {
                LoadIfNeeded();
                return _uvs;
            }
        }

        [JsonIgnore]
        public Vector3[] Normals
        {
            get
            {
                LoadIfNeeded();
                return _normals;
            }
        }

        private bool _hasLoaded = false;

        private Vector3[] _vertices;

        private int[] _triangles;

        private Vector2[] _uvs;

        private Vector3[] _normals;

        public BuiltInMeshProvider(string resourcePath)
        {
            ResourcePath = resourcePath;
        }

        private void LoadIfNeeded()
        {
            if (_hasLoaded)
            {
                return;
            }

            byte[] meshData = BuiltInAssetHelper.GetEmbeddedResource(ResourcePath);

            string objData = Encoding.UTF8.GetString(meshData);
            StringReader reader = new StringReader(objData);

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("v ")) //Vertex.
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
                else if (line.StartsWith("vt ")) //UVs.
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

            _hasLoaded = true;
        }


    }
}
