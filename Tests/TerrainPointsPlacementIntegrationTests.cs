using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Stages;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #301 "Alternating: Points" - the stage loop end to end: a fresh AI-vs-AI game with the mode
    // enabled must run the terrain roll-off, deal the allotments, alternate turns through the budget
    // loop (server-side affordability validation included) and terminate. The arithmetic itself is
    // pinned in TerrainPointsLedgerTests; this pins that the loop actually drives a real game.
    [TestFixture]
    public class TerrainPointsPlacementIntegrationTests
    {
        [Test]
        [CancelAfter(180_000)]
        public async Task PointsMode_FullAiGame_PerTurnOne_PlacesExactlyTheTotalPoints()
        {
            // Per-turn 1 makes the piece count exact: every turn budget is 1, and the AI prefers
            // debt-free picks, so it places precisely one 1-cost piece per turn - 6 points, 6 pieces.
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed: 4242, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 4242;
            settings.TerrainPlacementMode = ETerrainPlacementMode.AlternatingPoints;
            settings.TerrainPointsTotal = 6;
            settings.TerrainPointsPerTurn = 1;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task),
                "an AlternatingPoints game must play to completion - a hang means the points loop never terminated.");

            var tableState = new TableState(store);
            Assert.That(tableState.Terrain.Objects.Count, Is.EqualTo(6),
                "6 total points at 1 per turn with a debt-free AI is exactly six 1-cost pieces.");
        }

        // #393 - the companion at the OTHER end of the palette. The 1-point-per-turn test above can only
        // ever buy 1-cost pieces, so it exercises the small end exclusively; #393 grew the biggest template
        // from 11" to 11.5" and added ten more in that band, and the risk they carry is siting failure, not
        // arithmetic: a placer that cannot fit a big piece onto a filling table retries forever, and the
        // stage has no other way out. A 3-point turn budget puts the whole palette in reach.
        [Test]
        [CancelAfter(180_000)]
        public async Task PointsMode_WithABudgetThatReachesTheBigPieces_StillPlacesEveryPieceLegally()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed: 4242, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 4242;
            settings.TerrainPlacementMode = ETerrainPlacementMode.AlternatingPoints;
            settings.TerrainPointsTotal = 18;
            settings.TerrainPointsPerTurn = 3;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task),
                "a 3-point turn budget must still terminate - a hang here means the placer could not " +
                "find room for a big piece and kept retrying.");

            var tableState = new TableState(store);
            Assert.That(tableState.Terrain.Objects, Is.Not.Empty);

            // Every piece the placer committed must be legal where it sits: inside the table, and clear of
            // everything placed before it. Checked against the real validator, one piece at a time against
            // its predecessors, which is exactly the question the stage asked at commit time.
            var placed = new List<ITerrain>();
            foreach (ITerrain piece in tableState.Terrain.Objects)
            {
                Assert.That(
                    TerrainPlacementValidator.Check(piece.Shape,
                        GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                        GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, placed),
                    Is.EqualTo(TerrainPlacementValidity.Valid),
                    $"'{piece.Name}' was committed to an illegal position.");
                placed.Add(piece);
            }
        }
    }
}
