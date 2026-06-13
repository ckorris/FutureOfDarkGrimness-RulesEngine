using FDG.BuiltInAssets;
using System.Globalization;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;

namespace FDG.SerializableVisuals.Meshes
{
    [Serializable]
    public class BuiltInObjMeshProvider : IMeshProvider
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

        public BuiltInObjMeshProvider(string resourcePath)
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

            //Directly from the .obj.
            List<Vector3> srcVertices = new List<Vector3>();
            List<Vector2> srcUVs = new List<Vector2>();
            List<Vector3> srcNormals = new List<Vector3>();

            //Unified mesh data.
            List<Vector3> outVertices = new List<Vector3>();
            List<Vector2> outUVs = new List<Vector2>();
            List<Vector3> outNormals = new List<Vector3>();
            List<int> outTriangles = new List<int>();

            //Map (v,vt,vn) triplet to unified vertex index.
            Dictionary<(int v, int vt, int vn), int> vertexMap = new Dictionary<(int, int, int), int>();

            NumberFormatInfo nfi = CultureInfo.InvariantCulture.NumberFormat;

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue; //Empty line or a comment.
                }

                string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string tag = tokens[0];


                if (tag == "v") //Vertex.
                {
                    srcVertices.Add(new Vector3(float.Parse(tokens[1], nfi), float.Parse(tokens[2], nfi), float.Parse(tokens[3], nfi)));
                }
                else if (tag == "vt") //UVs.
                {
                    string[] parts = SplitWhitespace(line, 2);
                    string[] uv = SplitWhitespace(parts[1]);
                    srcUVs.Add(new Vector2(float.Parse(tokens[1], nfi), float.Parse(tokens[2], nfi)));

                }
                else if (tag == "vn") //Vertex normals.
                {
                    srcNormals.Add(new Vector3(float.Parse(tokens[1], nfi), float.Parse(tokens[2], nfi), float.Parse(tokens[3], nfi)));
                }
                else if (tag == "f") //Face.
                {
                    //Face with N corners; each corner token is v, v/vt, v//vn, or v/vt/vn.
                    //We triangulate as a fan: (0, i, i+1) for i=1..N-2.

                    int cornerCount = tokens.Length - 1;
                    if(cornerCount < 3)
                    {
                        continue; //Skipping degenerate face. TODO: Throw error?
                    }

                    int[] faceIdx = new int[cornerCount];
                    for(int c = 0; c < cornerCount; c++)
                    {
                        string corner = tokens[c + 1];
                        int vIndex;
                        int vtIndex;
                        int vnIndex;
                        ParseCorner(corner, srcVertices.Count, srcUVs.Count, srcNormals.Count,
                            out vIndex, out vtIndex, out vnIndex);

                        //Ugh, I don't like tuples but let's do this.
                        (int v, int vt, int vn) key = (vIndex, vtIndex, vnIndex);

                        int unifiedIndex;
                        if(vertexMap.TryGetValue(key, out unifiedIndex) == false)
                        {
                            //Create a new unified vertex.
                            Vector3 vertex = srcVertices[vIndex];
                            Vector2 uv = (vtIndex >= 0) ? srcUVs[vtIndex] : new Vector2(0f, 0f);
                            Vector3 normal = (vnIndex >= 0) ? srcNormals[vnIndex] : new Vector3(0f, 0f, 0f);

                            unifiedIndex = outVertices.Count;
                            vertexMap.Add(key, unifiedIndex);
                            outVertices.Add(vertex);
                            outUVs.Add(uv);
                            outNormals.Add(normal);
                        }

                        faceIdx[c] = unifiedIndex;
                    }

                    //Triangulate like a "folding fan". (Huh, this is a neat trick.)
                    for(int i = 1; i < cornerCount - 1; i++)
                    {
                        outTriangles.Add(faceIdx[0]);
                        outTriangles.Add(faceIdx[i + 1]);
                        outTriangles.Add(faceIdx[i]);

                    }
                }
            }


            //If there are any missing normals, recompute all of them. 
            //This seems like a waste, but if you fill in only some normals, things might look whack.
            //TODO


            //Assign unified arrays to fields.
            _vertices = outVertices.ToArray();
            _uvs = outUVs.ToArray();
            _normals = outNormals.ToArray();
            _triangles = outTriangles.ToArray();

            _hasLoaded = true;
        }

        private static string[] SplitWhitespace(string s, int maxParts = int.MaxValue)
        {
            return s.Split((char[])null, maxParts, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void ParseCorner(string token, int vCount, int vtCount, int vnCount, 
            out int v, out int vt, out int vn)
        {
            string[] parts = token.Split('/');
            v = ParseOneIndex(parts[0], vCount);

            if(parts.Length >= 2 && parts[1].Length > 0)
            {
                vt = ParseOneIndex(parts[1], vtCount);
            }
            else
            {
                vt = -1;
            }

            if(parts.Length >= 3 && parts[2].Length > 0)
            {
                vn = ParseOneIndex(parts[2], vnCount);
            }
            else
            {
                vn = -1;
            }
        }

        private static int ParseOneIndex(string s, int count)
        {
            //.obj is 1-based, so negative is relative to the end.
            int index = int.Parse(s, CultureInfo.InvariantCulture);

            if(index > 0)
            {
                return index - 1;
            }
            else
            {
                return count + index;
            }
        }
    }
}
