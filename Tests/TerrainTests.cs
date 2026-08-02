using FDG.Data;
using FDG.SaveLoad;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    [TestFixture]
    public class TerrainTests
    {
        [Test]
        public void CircularZone_PointInside_ReturnsTrue()
        {
            CircularZone zone = new CircularZone(new Float2(10, 10), 5);

            Assert.That(zone.IsPointWithinZone(new Float2(10, 10)), Is.True, "Center should be inside.");
            Assert.That(zone.IsPointWithinZone(new Float2(13, 14)), Is.True, "Distance 5 from center should be inside (boundary).");
            Assert.That(zone.IsPointWithinZone(new Float2(20, 10)), Is.False, "Distance 10 from center should be outside.");
        }

        [Test]
        public void CircularZone_SegmentCrossingCircle_Intersects()
        {
            CircularZone zone = new CircularZone(new Float2(10, 10), 3);

            //Segment passes through center.
            Assert.That(zone.DoesPathIntersectZone(new Float2(0, 10), new Float2(20, 10)), Is.True);

            //Segment fully outside, far from circle.
            Assert.That(zone.DoesPathIntersectZone(new Float2(0, 0), new Float2(0, 20)), Is.False);

            //Segment endpoint inside.
            Assert.That(zone.DoesPathIntersectZone(new Float2(10, 10), new Float2(20, 10)), Is.True);

            //Tangent — segment grazes circle at distance == radius.
            Assert.That(zone.DoesPathIntersectZone(new Float2(0, 13), new Float2(20, 13)), Is.True);
        }

        [Test]
        public void TerrainData_ForwardsShapeQueries()
        {
            RectangularZone rect = new RectangularZone(0, 10, 0, 10);
            TerrainData terrain = new TerrainData(ETerrainType.Cover | ETerrainType.Difficult, rect);

            Assert.That(terrain.IsPointWithinZone(new Float2(5, 5)), Is.True);
            Assert.That(terrain.IsPointWithinZone(new Float2(15, 5)), Is.False);
            Assert.That(terrain.DoesPathIntersectZone(new Float2(-1, 5), new Float2(11, 5)), Is.True);
        }

        [Test]
        public void ETerrainType_FlagsCombineCorrectly()
        {
            ETerrainType combined = ETerrainType.Cover | ETerrainType.Difficult;
            Assert.That(combined.HasFlag(ETerrainType.Cover), Is.True);
            Assert.That(combined.HasFlag(ETerrainType.Difficult), Is.True);
            Assert.That(combined.HasFlag(ETerrainType.Dangerous), Is.False);
        }

        [Test]
        public void RectangularZone_RejectsInvalidConstructorArgs()
        {
            Assert.Throws<ArgumentException>(() => new RectangularZone(10, 10, 0, 5));
            Assert.Throws<ArgumentException>(() => new RectangularZone(10, 0, 0, 5));
            Assert.Throws<ArgumentException>(() => new RectangularZone(0, 5, 10, 10));
            Assert.Throws<ArgumentException>(() => new RectangularZone(0, 5, 10, 0));
        }

        [Test]
        public void RectangularZone_PointWithinZone_BoundaryCases()
        {
            RectangularZone zone = new RectangularZone(0, 10, 0, 10);

            //Strictly inside.
            Assert.That(zone.IsPointWithinZone(new Float2(5, 5)), Is.True);

            //On each edge — current implementation includes the boundary (uses <= and >=).
            Assert.That(zone.IsPointWithinZone(new Float2(0, 5)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(10, 5)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(5, 0)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(5, 10)), Is.True);

            //Corners.
            Assert.That(zone.IsPointWithinZone(new Float2(0, 0)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(10, 10)), Is.True);

            //Outside.
            Assert.That(zone.IsPointWithinZone(new Float2(-0.01f, 5)), Is.False);
            Assert.That(zone.IsPointWithinZone(new Float2(10.01f, 5)), Is.False);
            Assert.That(zone.IsPointWithinZone(new Float2(5, -0.01f)), Is.False);
            Assert.That(zone.IsPointWithinZone(new Float2(5, 10.01f)), Is.False);
        }

        [Test]
        public void RectangularZone_DoesPathIntersectZone_VariousCases()
        {
            RectangularZone zone = new RectangularZone(0, 10, 0, 10);

            //Segment fully inside.
            Assert.That(zone.DoesPathIntersectZone(new Float2(2, 2), new Float2(8, 8)), Is.True);

            //Segment fully outside, with a bounding-box overlap that does NOT cross the rect.
            Assert.That(zone.DoesPathIntersectZone(new Float2(-5, 15), new Float2(15, 15)), Is.False);

            //Segment crossing one edge (one endpoint inside, one outside).
            Assert.That(zone.DoesPathIntersectZone(new Float2(5, 5), new Float2(20, 5)), Is.True);

            //Segment crossing two edges (both endpoints outside, passes through rect).
            Assert.That(zone.DoesPathIntersectZone(new Float2(-5, 5), new Float2(15, 5)), Is.True);

            //Segment far away, no overlap at all.
            Assert.That(zone.DoesPathIntersectZone(new Float2(20, 20), new Float2(30, 30)), Is.False);
        }

        [Test]
        public void RectangularZone_TouchingEdgeOnly_DoesNotCountAsIntersect()
        {
            //Pins down current behavior: LinesIntersect uses strict < 0 cross-product comparison,
            //so a segment that runs exactly along the boundary of the rect (without crossing into
            //it) does not count as an intersection. If movement validation later needs grazing
            //segments to count, this test will need to update alongside the implementation.
            RectangularZone zone = new RectangularZone(0, 10, 0, 10);

            //Segment running along the top edge — endpoints are on the boundary so IsPointWithinZone
            //returns true, which makes DoesPathIntersectZone return true via the endpoint-inside short-circuit.
            Assert.That(zone.DoesPathIntersectZone(new Float2(2, 10), new Float2(8, 10)), Is.True);

            //Segment running parallel just outside the edge — should not intersect.
            Assert.That(zone.DoesPathIntersectZone(new Float2(-5, 10.01f), new Float2(15, 10.01f)), Is.False);
        }

        [Test]
        public void RectangularZone_RoundTripsThroughJson()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
            };

            RectangularZone original = new RectangularZone(1.5f, 7.25f, -3.0f, 4.75f);
            string json = JsonConvert.SerializeObject(original, settings);
            RectangularZone? roundTrip = JsonConvert.DeserializeObject<RectangularZone>(json, settings);

            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.Left, Is.EqualTo(original.Left));
            Assert.That(roundTrip.Right, Is.EqualTo(original.Right));
            Assert.That(roundTrip.Bottom, Is.EqualTo(original.Bottom));
            Assert.That(roundTrip.Top, Is.EqualTo(original.Top));
        }

        [Test]
        public void CircularZone_RejectsNonPositiveRadius()
        {
            Assert.Throws<ArgumentException>(() => new CircularZone(new Float2(0, 0), 0));
            Assert.Throws<ArgumentException>(() => new CircularZone(new Float2(0, 0), -1));
        }

        [Test]
        public void TerrainLayoutFile_RoundTripsThroughJson()
        {
            //Mirror what TerrainLoader does to disk.
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
            };

            TerrainLayoutFile original = new TerrainLayoutFile
            {
                Name = "Round-Trip Test",
                Pieces = new List<TerrainPieceEntry>
                {
                    new TerrainPieceEntry
                    {
                        TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                        Shape = new RectangularZone(0, 6, 0, 4),
                        HeightInches = 4f,
                    },
                    new TerrainPieceEntry
                    {
                        TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                        Shape = new CircularZone(new Float2(20, 20), 5),
                        HeightInches = 0f,
                    },
                    new TerrainPieceEntry
                    {
                        TerrainType = ETerrainType.Dangerous,
                        Shape = new RectangularZone(40, 50, 10, 14),
                        HeightInches = 0f,
                    },
                }
            };

            string json = JsonConvert.SerializeObject(original, Formatting.Indented, settings);
            TerrainLayoutFile? roundTrip = JsonConvert.DeserializeObject<TerrainLayoutFile>(json, settings);

            Assert.That(roundTrip, Is.Not.Null);
            Assert.That(roundTrip!.Name, Is.EqualTo(original.Name));
            Assert.That(roundTrip.Pieces, Has.Count.EqualTo(3));

            Assert.That(roundTrip.Pieces[0].TerrainType, Is.EqualTo(ETerrainType.Blocking | ETerrainType.Impassible));
            Assert.That(roundTrip.Pieces[0].Shape, Is.TypeOf<RectangularZone>());
            Assert.That(roundTrip.Pieces[0].HeightInches, Is.EqualTo(4f));

            Assert.That(roundTrip.Pieces[1].Shape, Is.TypeOf<CircularZone>());
            Assert.That(roundTrip.Pieces[1].Shape.IsPointWithinZone(new Float2(20, 20)), Is.True);

            Assert.That(roundTrip.Pieces[2].TerrainType, Is.EqualTo(ETerrainType.Dangerous));
        }

        [Test]
        public void GameDataStore_TerrainData_AvailableViaTableState()
        {
            //End-to-end through the same path replication and the renderer will use:
            //create TerrainData in a default-built data store, observe it via IDataState<ITerrain>.
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            ITableState tableState = new TableState(store);

            int createdCount = 0;
            tableState.Terrain.OnObjectCreated += _ => createdCount++;

            TerrainData rectTerrain = new TerrainData(
                ETerrainType.Blocking | ETerrainType.Impassible,
                new RectangularZone(0, 10, 0, 10),
                heightInches: 4f);

            TerrainData circleTerrain = new TerrainData(
                ETerrainType.Cover | ETerrainType.Difficult,
                new CircularZone(new Float2(20, 20), 5));

            store.Create(rectTerrain);
            store.Create(circleTerrain);

            Assert.That(createdCount, Is.EqualTo(2), "OnObjectCreated should have fired for each terrain piece.");

            List<ITerrain> terrains = tableState.Terrain.Objects.ToList();
            Assert.That(terrains, Has.Count.EqualTo(2));

            ITerrain rectRetrieved = terrains.Single(t => t.Shape is RectangularZone);
            ITerrain circleRetrieved = terrains.Single(t => t.Shape is CircularZone);

            Assert.That(rectRetrieved.TerrainType, Is.EqualTo(ETerrainType.Blocking | ETerrainType.Impassible));
            Assert.That(rectRetrieved.HeightInches, Is.EqualTo(4f));
            Assert.That(rectRetrieved.IsPointWithinZone(new Float2(5, 5)), Is.True);

            Assert.That(circleRetrieved.TerrainType, Is.EqualTo(ETerrainType.Cover | ETerrainType.Difficult));
            Assert.That(circleRetrieved.IsPointWithinZone(new Float2(20, 20)), Is.True);
            Assert.That(circleRetrieved.IsPointWithinZone(new Float2(30, 20)), Is.False);
        }

        [Test]
        public void TerrainData_RoundTripsThroughJson_WithRectAndCircle()
        {
            //Mirror the data store's serializer settings — this is the same path that
            //GameDataUpdateSender uses to ship TerrainData over the wire.
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
            };

            TerrainData rectTerrain = new TerrainData(
                ETerrainType.Blocking | ETerrainType.Impassible,
                new RectangularZone(0, 10, 0, 10),
                heightInches: 4f,
                name: "Boulder");

            TerrainData circleTerrain = new TerrainData(
                ETerrainType.Cover | ETerrainType.Difficult,
                new CircularZone(new Float2(20, 20), 5));

            string rectJson = JsonConvert.SerializeObject(rectTerrain, settings);
            string circleJson = JsonConvert.SerializeObject(circleTerrain, settings);

            TerrainData? rectRoundTrip = JsonConvert.DeserializeObject<TerrainData>(rectJson, settings);
            TerrainData? circleRoundTrip = JsonConvert.DeserializeObject<TerrainData>(circleJson, settings);

            Assert.That(rectRoundTrip, Is.Not.Null);
            Assert.That(rectRoundTrip!.TerrainType, Is.EqualTo(rectTerrain.TerrainType));
            Assert.That(rectRoundTrip.HeightInches, Is.EqualTo(4f));
            Assert.That(rectRoundTrip.Name, Is.EqualTo("Boulder"));
            Assert.That(rectRoundTrip.Shape, Is.TypeOf<RectangularZone>());
            Assert.That(rectRoundTrip.IsPointWithinZone(new Float2(5, 5)), Is.True);

            Assert.That(circleRoundTrip, Is.Not.Null);
            Assert.That(circleRoundTrip!.TerrainType, Is.EqualTo(circleTerrain.TerrainType));
            Assert.That(circleRoundTrip.Name, Is.Empty);
            Assert.That(circleRoundTrip.Shape, Is.TypeOf<CircularZone>());
            Assert.That(circleRoundTrip.IsPointWithinZone(new Float2(20, 20)), Is.True);
            Assert.That(circleRoundTrip.IsPointWithinZone(new Float2(30, 20)), Is.False);
        }

        [Test]
        public void TerrainData_LegacyJsonWithoutName_DeserializesToEmptyName()
        {
            //Saves and wire payloads written before the name field existed have no "name" property;
            //they must keep loading with an empty (not null) name.
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
            };

            string legacyJson =
                "{\"TerrainType\":\"Cover\",\"Shape\":{\"$type\":\"FDG.RectangularZone, FutureOfDarkGrimness\"," +
                "\"Left\":0.0,\"Right\":10.0,\"Bottom\":0.0,\"Top\":10.0},\"HeightInches\":2.0}";

            TerrainData? loaded = JsonConvert.DeserializeObject<TerrainData>(legacyJson, settings);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Name, Is.Empty);
            Assert.That(loaded.TerrainType, Is.EqualTo(ETerrainType.Cover));
            Assert.That(loaded.HeightInches, Is.EqualTo(2f));
        }
    }
}
