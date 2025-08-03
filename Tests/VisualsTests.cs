using FDG.BuiltInAssets;
using FDG.SerializableVisuals.Meshes;
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
    }
}
