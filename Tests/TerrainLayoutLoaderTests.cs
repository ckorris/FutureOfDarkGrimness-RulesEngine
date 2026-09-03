using System;
using System.IO;
using FDG.SaveLoad;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    // #67: TerrainLayoutLoader.TryLoadFromFile is the content parser for hand-authored/shared
    // .fdgterrain files - untrusted input (see AllowlistSerializationBinderTests for the $type
    // gating it rides on). Had no dedicated tests. Pins the happy path plus every error path
    // returning a non-null, displayable `error` string instead of throwing out to the caller.
    [TestFixture]
    public class TerrainLayoutLoaderTests
    {
        private string _tempDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "fdg-terrain-loader-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        private string WriteFile(string contents, string fileName = "layout.fdgterrain")
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        [Test]
        public void TryLoadFromFile_MissingFile_ReturnsNullWithDisplayableError()
        {
            string missingPath = Path.Combine(_tempDir, "does-not-exist.fdgterrain");

            TerrainLayoutFile? result = TerrainLayoutLoader.TryLoadFromFile(missingPath, out string? error);

            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(error, Does.Contain(missingPath), "the error should name the path a user typed/picked");
        }

        [Test]
        public void TryLoadFromFile_MalformedJson_ReturnsNullWithDisplayableError_NotThrow()
        {
            string path = WriteFile("{ this is not valid json ");

            TerrainLayoutFile? result = null;
            string? error = null;
            Assert.DoesNotThrow(() => result = TerrainLayoutLoader.TryLoadFromFile(path, out error),
                "a malformed/hand-edited file must fail gracefully, not crash the caller");

            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TryLoadFromFile_JsonNullLiteral_ReturnsNullWithDisplayableError()
        {
            string path = WriteFile("null");

            TerrainLayoutFile? result = TerrainLayoutLoader.TryLoadFromFile(path, out string? error);

            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void TryLoadFromFile_ValidLayout_RoundTripsNameAndPieces()
        {
            var original = new TerrainLayoutFile
            {
                Name = "Test Layout",
                Pieces =
                {
                    new TerrainPieceEntry
                    {
                        Name = "Boulder",
                        TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                        Shape = new RectangularZone(0f, 4f, 0f, 4f),
                        HeightInches = 1.5f,
                        Points = 2,
                    },
                },
            };
            string json = JsonConvert.SerializeObject(original,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
            string path = WriteFile(json);

            TerrainLayoutFile? loaded = TerrainLayoutLoader.TryLoadFromFile(path, out string? error);

            Assert.That(error, Is.Null);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.EqualTo("Test Layout"));
            Assert.That(loaded.Pieces, Has.Count.EqualTo(1));
            Assert.That(loaded.Pieces[0].Name, Is.EqualTo("Boulder"));
            Assert.That(loaded.Pieces[0].TerrainType, Is.EqualTo(ETerrainType.Cover | ETerrainType.Difficult));
            Assert.That(loaded.Pieces[0].HeightInches, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(loaded.Pieces[0].Points, Is.EqualTo(2));
            Assert.That(loaded.Pieces[0].Shape, Is.TypeOf<RectangularZone>(),
                "the polymorphic IZone must resolve back to its concrete shape via the allowlisted binder");
            var rect = (RectangularZone)loaded.Pieces[0].Shape;
            Assert.That(rect.Right, Is.EqualTo(4f).Within(0.0001f));
        }
    }
}
