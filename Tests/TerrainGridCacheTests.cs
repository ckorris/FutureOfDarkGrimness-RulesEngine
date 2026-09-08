using FDG.Ai.Tactician;
using FDG.Data;
using NUnit.Framework;

namespace FDG.Tests
{
    // #191 perf pass: TerrainGrid is memoized because per-activation rebuilds were ~half a horde
    // game's CPU on dense maps. Re-keyed by the 2026-09-08 search perf pass: the key is the IDENTITY of
    // the terrain pieces (plus radius and the Strider flag), not the table state, so the thousands of
    // snapshots a search materializes - which share the live game's immutable TerrainData instances -
    // share one grid. These pin the cache CONTRACT - hits return the same instance, every key component
    // misses to a fresh build - because a wrong hit here silently routes a unit through the wrong map.
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
            TerrainGrid first = TerrainGridCache.Get(OneWall, 0.5f);
            TerrainGrid second = TerrainGridCache.Get(OneWall, 0.5f);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void DifferentRadiusOrStriderFlag_MissesToAFreshGrid()
        {
            TerrainGrid narrow = TerrainGridCache.Get(OneWall, 0.5f);
            Assert.That(TerrainGridCache.Get(OneWall, 1.0f), Is.Not.SameAs(narrow));
            Assert.That(TerrainGridCache.Get(OneWall, 0.5f, ignoreDifficultTerrain: true),
                Is.Not.SameAs(narrow));
        }

        [Test]
        public void ChangedTerrainCount_MissesToAFreshGrid()
        {
            TerrainGrid before = TerrainGridCache.Get(OneWall, 0.5f);
            ITerrain[] grown =
            {
                OneWall[0],
                new TerrainData(ETerrainType.Impassible, new RectangularZone(30f, 34f, 10f, 20f)),
            };
            Assert.That(TerrainGridCache.Get(grown, 0.5f), Is.Not.SameAs(before));
        }

        [Test]
        public void SameTerrainInstances_ShareOneGrid_AcrossTableStates()
        {
            // The search's case: every materialized snapshot is a new table state over the SAME
            // immutable TerrainData instances (StoreClone copies them by reference).
            _ = NewTableState();
            TerrainGrid a = TerrainGridCache.Get(OneWall, 0.5f);
            _ = NewTableState();
            TerrainGrid b = TerrainGridCache.Get(OneWall, 0.5f);
            Assert.That(b, Is.SameAs(a));
        }

        [Test]
        public void EqualButDistinctTerrainInstances_NeverShareAnEntry()
        {
            // A different game that happens to have the same layout has its own instances - and a
            // JSON-loaded snapshot has fresh instances too. Identity, not content, is the key: content
            // hashing an IZone is not something this cache wants to be responsible for getting right.
            ITerrain[] twin = { new TerrainData(ETerrainType.Impassible, new RectangularZone(10f, 14f, 10f, 20f)) };
            TerrainGrid a = TerrainGridCache.Get(OneWall, 0.5f);
            TerrainGrid b = TerrainGridCache.Get(twin, 0.5f);
            Assert.That(b, Is.Not.SameAs(a));
        }

        [Test]
        public void MoreThanCapacityTerrainSets_EvictsTheOldest()
        {
            ITerrain[] first = { new TerrainData(ETerrainType.Impassible, new RectangularZone(1f, 2f, 1f, 2f)) };
            TerrainGrid firstGrid = TerrainGridCache.Get(first, 0.5f);
            for (int i = 0; i < TerrainGridCache.Capacity; i++)
            {
                ITerrain[] set = { new TerrainData(ETerrainType.Impassible, new RectangularZone(3f + i, 4f + i, 1f, 2f)) };
                TerrainGridCache.Get(set, 0.5f);
            }
            Assert.That(TerrainGridCache.Count, Is.LessThanOrEqualTo(TerrainGridCache.Capacity));
            Assert.That(TerrainGridCache.Get(first, 0.5f), Is.Not.SameAs(firstGrid),
                "the oldest set was evicted, so asking again builds a fresh grid");
        }
    }
}
