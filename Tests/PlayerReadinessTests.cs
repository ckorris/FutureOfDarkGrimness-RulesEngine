using System;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using NUnit.Framework;

namespace FDG.Tests
{
    // #036 — server readiness handshake. The host must not enter the state machine until every assigned
    // slot's controller reports ready (a network player when its client sends the post-launch ready
    // message, a local player when its resolvers are assigned, an AI immediately). These pin the
    // PlayerSlotManager.WaitUntilAllSlotsReady contract that FDGServer.LaunchStateMachineOnceReady awaits.
    [TestFixture]
    public class PlayerReadinessTests
    {
        private static GameDataStore NewStore() =>
            new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(8)
                .Build();

        private static PlayerSlot NewSlot(GameDataStore store, int slotId, IPlayerController controller)
        {
            var slot = new PlayerSlot(slotId, teamNumber: slotId, new PlayerID(Guid.NewGuid()),
                armyListFile: null, store);
            slot.AssignPlayerController(controller);
            return slot;
        }

        private static async Task<bool> CompletesWithin(Task task, int milliseconds)
        {
            return await Task.WhenAny(task, Task.Delay(milliseconds)) == task;
        }

        [Test]
        public async Task WaitUntilAllSlotsReady_BlocksUntilEverySlotIsReady()
        {
            GameDataStore store = NewStore();
            var controllerA = new ControllablePlayerController("A");
            var controllerB = new ControllablePlayerController("B");
            var manager = new PlayerSlotManager(new[]
            {
                NewSlot(store, 0, controllerA),
                NewSlot(store, 1, controllerB),
            });

            Task waitTask = manager.WaitUntilAllSlotsReady();
            Assert.That(waitTask.IsCompleted, Is.False, "must not complete while both players are unready.");

            controllerA.MarkReady();
            Assert.That(waitTask.IsCompleted, Is.False, "must not complete while one player is still unready.");

            controllerB.MarkReady();
            Assert.That(await CompletesWithin(waitTask, 2000), Is.True,
                "completes once the last slot's controller reports ready.");
        }

        [Test]
        public async Task WaitUntilAllSlotsReady_CompletesImmediately_WhenAllAlreadyReady()
        {
            GameDataStore store = NewStore();
            var manager = new PlayerSlotManager(new[]
            {
                NewSlot(store, 0, new ControllablePlayerController("A", startsReady: true)),
                NewSlot(store, 1, new ControllablePlayerController("B", startsReady: true)),
            });

            Task waitTask = manager.WaitUntilAllSlotsReady();

            Assert.That(await CompletesWithin(waitTask, 2000), Is.True,
                "all-ready slots (e.g. AI / already-launched locals) gate nothing.");
        }

        [Test]
        public void WaitUntilAllSlotsReady_Throws_WhenASlotIsUnfilled()
        {
            GameDataStore store = NewStore();
            var unfilledSlot = new PlayerSlot(0, teamNumber: 0, new PlayerID(Guid.NewGuid()),
                armyListFile: null, store); // no controller assigned
            var manager = new PlayerSlotManager(new[] { unfilledSlot });

            Assert.That(() => manager.WaitUntilAllSlotsReady(), Throws.InvalidOperationException,
                "launching before all slots are crewed is a programming error, surfaced loudly.");
        }

        /// <summary>
        /// An IPlayerController whose readiness is driven by the test, mirroring the real controllers'
        /// WaitUntilReadyAsync (completed once ready; otherwise a TCS that fires on OnReadyStateChanged).
        /// </summary>
        private sealed class ControllablePlayerController : IPlayerController
        {
            public string Name { get; }
            public PlayerID ID { get; } = new PlayerID(Guid.NewGuid());
            public bool IsReady { get; private set; }
            public IPresentationSink? PresentationSink => null;

            public event Action<bool>? OnReadyStateChanged;
            public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;

            public ControllablePlayerController(string name, bool startsReady = false)
            {
                Name = name;
                IsReady = startsReady;
            }

            public void MarkReady()
            {
                if (IsReady)
                {
                    return;
                }

                IsReady = true;
                OnReadyStateChanged?.Invoke(true);
            }

            public Task WaitUntilReadyAsync()
            {
                if (IsReady)
                {
                    return Task.CompletedTask;
                }

                var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void Handler(bool ready)
                {
                    if (ready == false)
                    {
                        return;
                    }

                    OnReadyStateChanged -= Handler;
                    source.SetResult(true);
                }

                OnReadyStateChanged += Handler;
                return source.Task;
            }

            public void SendLogMessage(string logMessage, TextColor color) { }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }
    }
}
