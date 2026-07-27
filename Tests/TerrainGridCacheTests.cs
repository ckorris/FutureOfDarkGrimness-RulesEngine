using FDG.Ai.Tactician;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 perf pass: TerrainGrid is memoized per game because per-activation rebuilds were ~half a
    // horde game's CPU on dense maps. These pin the cache CONTRACT - hits return the same instance,
    // every key component (radius, Strider flag, terrain count, table state) misses to a fresh
    // build - because a wrong hit here silently routes a unit through the wrong map.
    [TestFixture]
    public class TerrainGridCacheTests
    {
        private static TableState NewTableState() =>
            new TableState(GameDataStore.GameDataStoreBuilder.GetDefault());

        private static readonly ITerrain[] OneWall =
        {
            new TerrainData(ETerrainType.Impassible, new RectangularZone(10f, 14f, 10f, 20f)),
        };

        [Test]
        public void SameGameSameInputs_ReturnsTheCachedInstance()
        {
            TableState state = NewTableState();
            TerrainGrid first = TerrainGridCache.Get(state, OneWall, 0.5f);
            TerrainGrid second = TerrainGridCache.Get(state, OneWall, 0.5f);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void DifferentRadiusOrStriderFlag_MissesToAFreshGrid()
        {
            TableState state = NewTableState();
            TerrainGrid narrow = TerrainGridCache.Get(state, OneWall, 0.5f);
            Assert.That(TerrainGridCache.Get(state, OneWall, 1.0f), Is.Not.SameAs(narrow));
            Assert.That(TerrainGridCache.Get(state, OneWall, 0.5f, ignoreDifficultTerrain: true),
                Is.Not.SameAs(narrow));
        }

        [Test]
        public void ChangedTerrainCount_MissesToAFreshGrid()
        {
            TableState state = NewTableState();
            TerrainGrid before = TerrainGridCache.Get(state, OneWall, 0.5f);
            ITerrain[] grown =
            {
                OneWall[0],
                new TerrainData(ETerrainType.Impassible, new RectangularZone(30f, 34f, 10f, 20f)),
            };
            Assert.That(TerrainGridCache.Get(state, grown, 0.5f), Is.Not.SameAs(before));
        }

        [Test]
        public void DifferentGames_NeverShareEntries()
        {
            TerrainGrid a = TerrainGridCache.Get(NewTableState(), OneWall, 0.5f);
            TerrainGrid b = TerrainGridCache.Get(NewTableState(), OneWall, 0.5f);
            Assert.That(b, Is.Not.SameAs(a));
        }
    }
}
