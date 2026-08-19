using FDG.BuiltInAssets;
using FDG.SerializableVisuals.Materials;
using FDG.SerializableVisuals;
using FDG.SerializableVisuals.Meshes;
using Newtonsoft.Json;
using NUnit.Framework;
using System.Numerics;

namespace FDG.Tests
{
    [TestFixture]
    public class VisualsTests
    {
        [Test]
        public void BuiltInObjMeshProviderLoadingAndParsing()
        {
            string resourcePath = BuiltInAssetHelper.SILLYMANMODEL_PATH;

            var meshProvider = new BuiltInObjMeshProvider(resourcePath);

            int vertexCount = meshProvider.Vertices.Length;
            int triangleCount = meshProvider.Triangles.Length;
            int uvCount = meshProvider.UVs.Length;
            int normalCount = meshProvider.Normals.Length;

            Console.WriteLine($"Total Vertices: {vertexCount}");
            Console.WriteLine($"Total Triangles: {triangleCount}");
            Console.WriteLine($"Total UVs: {uvCount}");
            Console.WriteLine($"Total Normals: {normalCount}");

            Assert.That(vertexCount, Is.GreaterThan(0), "Vertex count is zero.");
            Assert.That(triangleCount, Is.GreaterThan(0), "Triangle count is zero.");
            Assert.That(uvCount, Is.GreaterThan(0), "UV count is zero.");
            Assert.That(normalCount, Is.GreaterThan(0), "Normal count is zero.");
        }

        [Test]
        public void BuiltInObjMeshProviderSerializationAndDeserialization()
        {
            string resourcePath = BuiltInAssetHelper.SILLYMANMODEL_PATH;

            var originalMeshProvider = new BuiltInObjMeshProvider(resourcePath);

            string serializedJson = JsonConvert.SerializeObject(originalMeshProvider);

            var deserializedMeshProvider = JsonConvert.DeserializeObject<BuiltInObjMeshProvider>(serializedJson);

            var vertices = deserializedMeshProvider.Vertices;
            var triangles = deserializedMeshProvider.Triangles;
            var uvs = deserializedMeshProvider.UVs;
            var normals = deserializedMeshProvider.Normals;

            Console.WriteLine($"Total Vertices: {vertices.Length}");
            Console.WriteLine($"Total Triangles: {triangles.Length}");
            Console.WriteLine($"Total UVs: {uvs.Length}");
            Console.WriteLine($"Total Normals: {normals.Length}");

            Assert.That(vertices.Length, Is.GreaterThan(0), "Vertex count is zero after deserialization.");
            Assert.That(triangles.Length, Is.GreaterThan(0), "Triangle count is zero after deserialization.");
            Assert.That(uvs.Length, Is.GreaterThan(0), "UV count is zero after deserialization.");
            Assert.That(normals.Length, Is.GreaterThan(0), "Normal count is zero after deserialization.");
        }


        [Test]
        public void TestMaterialSerializationAndDeserialization()
        {
            var originalMaterial = new BasicMaterial
            {
                BaseColor = new Vector4(1f, 0f, 0f, 1f), // Red color
                EmissiveColor = null,
                BaseColorTexture = null,
                NormalMapTexture = null,
                RoughnessMapTexture = null,
                MetallicMapTexture = null,
                SpecularMapTexture = null,
                EmissionMapTexture = null
            };

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                //Ensures that the proper type is used during serialization.
                TypeNameHandling = TypeNameHandling.Auto
            };

            string serializedJson = JsonConvert.SerializeObject(originalMaterial, settings);

            IMaterialProvider deserializedMaterial = JsonConvert.DeserializeObject<BasicMaterial>(serializedJson, settings);

            Assert.That(deserializedMaterial.BaseColor, Is.EqualTo(originalMaterial.BaseColor), "BaseColor doesn't match.");
            Assert.That(deserializedMaterial.EmissiveColor, Is.Null, "EmissiveColor should be null.");
            Assert.That(deserializedMaterial.BaseColorTexture, Is.Null, "BaseColorTexture should be null.");
        }
    }
}
