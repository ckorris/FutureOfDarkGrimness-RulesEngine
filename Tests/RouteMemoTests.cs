using FDG.Ai.Tactician;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #191 search perf pass: routes are memoized on the (shared, immutable) TerrainGrid. What must hold
    /// is that a hit is indistinguishable from a miss to the caller - same waypoints, a list the caller
    /// may mutate without poisoning the memo - and that the two pathfinders never serve each other.
    /// </summary>
    [TestFixture]
    public class RouteMemoTests
    {
        // A wall across the middle of a corridor: start and goal on opposite sides, so the straight
        // line is blocked and the A* body actually runs.
        private static readonly ITerrain[] Wall =
        {
            new TerrainData(ETerrainType.Impassible, new RectangularZone(10f, 14f, 5f, 25f)),
        };
        private static readonly Position Start = new(5f, 15f);
        private static readonly Position Goal = new(20f, 15f);

        [Test]
        public void SecondAsk_ReturnsTheSameRoute_AsADistinctList()
        {
            TerrainGrid grid = TerrainGrid.Build(Wall, 0.5f);
            List<Position>? first = GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f);
            List<Position>? second = GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null.And.Not.SameAs(first), "a hit hands back the caller's own list");
            Assert.That(second, Is.EqualTo(first), "and the same waypoints, first to last");
            Assert.That(first!.Count, Is.GreaterThan(2), "the wall forces a detour, so this is a real route");
            Assert.That(grid.RouteMemoCount, Is.EqualTo(1));
        }

        [Test]
        public void MutatingAReturnedRoute_DoesNotPoisonTheMemo()
        {
            TerrainGrid grid = TerrainGrid.Build(Wall, 0.5f);
            List<Position> first = GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f)!;
            var expected = new List<Position>(first);
            first.Clear();
            first.Add(new Position(0f, 0f));

            List<Position> again = GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f)!;

            Assert.That(again, Is.EqualTo(expected));
        }

        [Test]
        public void DifferentEndpointsOrRadius_AreDifferentEntries()
        {
            TerrainGrid grid = TerrainGrid.Build(Wall, 0.5f);
            GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f);
            GridPathfinder.FindPath(grid, Wall, Start, Goal, 1.0f);
            GridPathfinder.FindPath(grid, Wall, Start, new Position(20f, 16f), 0.5f);
            GridPathfinder.FindPath(grid, Wall, new Position(5f, 15f, 1f), Goal, 0.5f); // y differs

            Assert.That(grid.RouteMemoCount, Is.EqualTo(4));
        }

        [Test]
        public void TheTwoPathfinders_NeverServeEachOther()
        {
            TerrainGrid grid = TerrainGrid.Build(Wall, 0.5f);
            List<Position>? direct = GridPathfinder.FindPath(grid, Wall, Start, Goal, 0.5f);
            List<Position>? nearest = GridPathfinder.FindPathToNearestReachable(grid, Wall, Start, Goal, 0.5f);

            Assert.That(grid.RouteMemoCount, Is.EqualTo(2), "one entry per pathfinder for the same key");
            Assert.That(direct, Is.Not.Null);
            Assert.That(nearest, Is.Not.Null);
        }
    }
}
