using FDG.BuiltInAssets;
using FDG.SerializableVisuals.Meshes;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class VisualsTests
    {
        [Test]
        public void TestMeshLoadingAndParsing()
        {
            // The resource path to your mesh file (relative to your project's namespace).
            string resourcePath = BuiltInAssetHelper.SILLYMANMODEL_PATH;

            // Create an instance of BuiltInMeshProvider with the resource path.
            var meshProvider = new BuiltInMeshProvider(resourcePath);

            // Get the counts of the various mesh components.
            int vertexCount = meshProvider.Vertices.Length;
            int triangleCount = meshProvider.Triangles.Length;
            int uvCount = meshProvider.UVs.Length;
            int normalCount = meshProvider.Normals.Length;

            // Print out the totals for inspection.
            Console.WriteLine($"Total Vertices: {vertexCount}");
            Console.WriteLine($"Total Triangles: {triangleCount}");
            Console.WriteLine($"Total UVs: {uvCount}");
            Console.WriteLine($"Total Normals: {normalCount}");

            // Assert that none of the counts are zero (to ensure the mesh was loaded).
            Assert.That(vertexCount, Is.GreaterThan(0), "Vertex count is zero.");
            Assert.That(triangleCount, Is.GreaterThan(0), "Triangle count is zero.");
            Assert.That(uvCount, Is.GreaterThan(0), "UV count is zero.");
            Assert.That(normalCount, Is.GreaterThan(0), "Normal count is zero.");
        }

        [Test]
        public void TestSerializationAndDeserialization()
        {
            // The resource path to your mesh file (relative to your project's namespace).
            string resourcePath = BuiltInAssetHelper.SILLYMANMODEL_PATH;

            // Create an instance of BuiltInMeshProvider with the resource path.
            var originalMeshProvider = new BuiltInMeshProvider(resourcePath);

            // Serialize the original object to a JSON string
            string serializedJson = JsonConvert.SerializeObject(originalMeshProvider);

            // Deserialize it back to a BuiltInMeshProvider object
            var deserializedMeshProvider = JsonConvert.DeserializeObject<BuiltInMeshProvider>(serializedJson);

            // Access the properties to trigger lazy loading
            var vertices = deserializedMeshProvider.Vertices;
            var triangles = deserializedMeshProvider.Triangles;
            var uvs = deserializedMeshProvider.UVs;
            var normals = deserializedMeshProvider.Normals;

            // Print the totals for inspection (optional, remove for actual testing)
            Console.WriteLine($"Total Vertices: {vertices.Length}");
            Console.WriteLine($"Total Triangles: {triangles.Length}");
            Console.WriteLine($"Total UVs: {uvs.Length}");
            Console.WriteLine($"Total Normals: {normals.Length}");

            // Assert that none of the counts are zero (to ensure lazy loading worked and mesh is valid)
            Assert.That(vertices.Length, Is.GreaterThan(0), "Vertex count is zero after deserialization.");
            Assert.That(triangles.Length, Is.GreaterThan(0), "Triangle count is zero after deserialization.");
            Assert.That(uvs.Length, Is.GreaterThan(0), "UV count is zero after deserialization.");
            Assert.That(normals.Length, Is.GreaterThan(0), "Normal count is zero after deserialization.");
        }
    }
}
