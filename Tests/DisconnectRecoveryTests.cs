using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Network.Connection;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #187 — the recovery half of the disconnect lifecycle (#076 / <see cref="DisconnectLifecycleTests"/>
    /// covers the messaging half). A real game is stood up with one networked player, that player's
    /// connection drops, and the two facts the host's auto-save depends on are pinned:
    ///
    /// <list type="number">
    /// <item>the game ends with <see cref="EGameOutcome.Disconnect"/>, not <see cref="EGameOutcome.Fault"/> —
    /// that is what tells the front end to write a recovery file instead of reporting an engine bug;</item>
    /// <item>the store the host serializes at that moment really does round-trip back into a resumable
    /// save. The state machine has finished unwinding by then, so this is the claim the whole feature
    /// rests on: a dropped game is recoverable, not merely reported.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class DisconnectRecoveryTests
    {
        [Test]
        [CancelAfter(120_000)]
        public async Task NetworkPlayerDrops_GameEndsWithDisconnectOutcome()
        {
            (GameResult result, _) = await PlayUntilNetworkPlayerDrops();

            Assert.That(result.Outcome, Is.EqualTo(EGameOutcome.Disconnect),
                "a dropped connection is its own ending - the host keys the recovery save off this.");
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault),
                "a player leaving is not an engine fault.");
            Assert.That(result.Message, Does.Contain("Mrs. Client"), "the ending names who left.");
        }

        // The reason Disconnect is worth splitting from Fault: unlike a faulted store, this one is intact.
        [Test]
        [CancelAfter(120_000)]
        public async Task StoreAtDisconnect_RoundTripsIntoAResumableSave()
        {
            (GameResult result, GameDataStore store) = await PlayUntilNetworkPlayerDrops();
            Assert.That(result.Outcome, Is.EqualTo(EGameOutcome.Disconnect));

            string json = GameSaveSerializer.Save(store);
            GameDataStore reloaded = GameSaveSerializer.Load(json);

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(reloaded);
            Assert.That(progress, Is.Not.Null, "a recovery save must carry the flow state a resume needs.");
            Assert.That(progress!.RoundCount, Is.GreaterThan(0), "the resumed round must be a real round.");
            Assert.That(reloaded.GetAllValues<UnitData>().Any(), Is.True,
                "the armies must survive into the recovery save.");
            Assert.That(reloaded.GetAllValues<PlayerSlotInfo>().Count(), Is.EqualTo(2),
                "both players' slots must survive, so the dropped player has a slot to rejoin.");
        }

        // Stands up a two-player game from a compiled scenario (slot 0 AI, slot 1 networked), marks the
        // network player ready so the state machine launches, then drops its connection. The game ends at
        // the first decision asked of that player - which is the point of #187 slice 2.
        private static async Task<(GameResult Result, GameDataStore Store)> PlayUntilNetworkPlayerDrops()
        {
            GameDataStore store = ScenarioCompiler.Compile(MakeScenario(),
                new[] { MakeArmy("Reds"), MakeArmy("Blues") });

            List<PlayerSlotInfo> savedInfos = store.GetAllValues<PlayerSlotInfo>()
                .OrderBy(info => info.SlotID).ToList();
            foreach (DataReference oldInfo in store.GetAllDataReferences<PlayerSlotInfo>().ToList())
                store.Destroy(oldInfo);

            var bus = new InProcessBus();
            var slots = new PlayerSlot[savedInfos.Count];

            // Slot 0: an AI that will happily keep playing - so the game only ends because of the drop.
            slots[0] = new PlayerSlot(0, savedInfos[0].TeamNumber, savedInfos[0].PlayerID, new ArmyListFile(), store);
            var aiGame = new FDGGame_AsLocal(store, bus);
            slots[0].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                "AI 0", savedInfos[0].PlayerID, aiGame, seed: 5150, slotID: slots[0].SlotID));

            // Slot 1: the remote player who is about to lose their connection.
            var connectionID = new ConnectionID(Guid.NewGuid());
            slots[1] = new PlayerSlot(1, savedInfos[1].TeamNumber, savedInfos[1].PlayerID, new ArmyListFile(), store);
            slots[1].AssignPlayerController(new NetworkPlayerController("Mrs. Client", savedInfos[1].PlayerID,
                connectionID, bus, store));

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            // A networked slot is only "ready" once its client reports in post-launch (#036); without this
            // the server waits forever and never reaches a decision to fault.
            await bus.SendCommandToAllAsync(new PostLaunchPlayerReadyMessage(savedInfos[1].PlayerID));

            // ...and then the connection drops. Whether or not a request is pending for them right now,
            // the next one targeting this player faults (#187 slice 2).
            bus.SimulateClientDisconnected(connectionID);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(90)));
            Assert.That(finished, Is.SameAs(completed.Task),
                "the dropped connection must end the game, not hang it.");
            return (await completed.Task, store);
        }

        private static ScenarioFile MakeScenario() => new ScenarioFile
        {
            Name = "Disconnect recovery",
            Round = 2,
            ActivePlayer = 0,
            Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 5150 },
            Players = new()
            {
                new ScenarioPlayer
                {
                    Army = "reds.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Troops",
                            Models = new() { new[] { 20f, 20f }, new[] { 21f, 20f }, new[] { 22f, 20f } },
                        },
                    },
                },
                new ScenarioPlayer
                {
                    Army = "blues.fdgarmy",
                    Units = new()
                    {
                        new ScenarioUnit
                        {
                            Unit = "Troops",
                            Models = new() { new[] { 40f, 26f }, new[] { 41f, 26f }, new[] { 42f, 26f } },
                        },
                    },
                },
            },
        };

        private static ArmyListFile MakeArmy(string name) => new ArmyListFile
        {
            Name = name,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Troops", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };
    }
}
