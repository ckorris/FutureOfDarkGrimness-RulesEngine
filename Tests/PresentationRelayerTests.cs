using System;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using NUnit.Framework;

namespace FDG.Tests
{
    // PresentationRelayer is the server-side IPresenter on the game context: it must fan each beat
    // out to every player's sink and then wait on the clock exactly ONCE (a single host-side wait is
    // what gives all front-ends a shared tempo and keeps remote clients from building a backlog).
    [TestFixture]
    public class PresentationRelayerTests
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

        [Test]
        public async Task Present_FansBeatToEverySink_AndWaitsOnceOnClock()
        {
            GameDataStore store = NewStore();
            var sinkA = new RecordingPresentationSink();
            var sinkB = new RecordingPresentationSink();

            var slots = new[]
            {
                NewSlot(store, 0, new TestPlayerController("A", sinkA)),
                NewSlot(store, 1, new TestPlayerController("B", sinkB)),
            };
            var psm = new PlayerSlotManager(slots);
            var clock = new FakePresentationClock();
            var relayer = new PresentationRelayer(psm, clock);

            var beat = new TestBeat(TimeSpan.FromMilliseconds(200));
            await relayer.Present(beat);

            Assert.That(sinkA.Beats, Is.EqualTo(new[] { beat }), "every player's sink receives the beat");
            Assert.That(sinkB.Beats, Is.EqualTo(new[] { beat }));
            Assert.That(clock.Waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(200) }),
                "the relayer waits once for the whole fan-out, not once per player");
        }

        [Test]
        public async Task Present_SkipsNullSinksAndEmptySlots_AndStillPacesOnce()
        {
            GameDataStore store = NewStore();
            var sink = new RecordingPresentationSink();

            var withSink = NewSlot(store, 0, new TestPlayerController("Human", sink));
            var withoutSink = NewSlot(store, 1, new TestPlayerController("AI", presentationSink: null)); // e.g. computer player
            var empty = new PlayerSlot(2, 2, new PlayerID(Guid.NewGuid()), armyListFile: null, store); // no controller

            var psm = new PlayerSlotManager(new[] { withSink, withoutSink, empty });
            var clock = new FakePresentationClock();
            var relayer = new PresentationRelayer(psm, clock);

            var beat = new TestBeat(TimeSpan.FromMilliseconds(120));
            Assert.DoesNotThrowAsync(async () => await relayer.Present(beat));

            Assert.That(sink.Beats, Is.EqualTo(new[] { beat }));
            Assert.That(clock.Waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(120) }));
        }

        /// <summary>Minimal IPlayerController whose only meaningful member is its presentation sink.</summary>
        private sealed class TestPlayerController : IPlayerController
        {
            public TestPlayerController(string name, IPresentationSink? presentationSink)
            {
                Name = name;
                PresentationSink = presentationSink;
            }

            public string Name { get; }
            public PlayerID ID { get; } = new PlayerID(Guid.NewGuid());
            public bool IsReady => true;
            public IPresentationSink? PresentationSink { get; }

            public event Action<bool>? OnReadyStateChanged;
            public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;

            public Task WaitUntilReadyAsync() => Task.CompletedTask;
            public void SendLogMessage(string logMessage, TextColor color) { }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }
    }
}
