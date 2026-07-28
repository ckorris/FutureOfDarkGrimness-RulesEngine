using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    // #299 "Alternating: Points" - the stage loop end to end: a fresh AI-vs-AI game with the mode
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
    }
}
