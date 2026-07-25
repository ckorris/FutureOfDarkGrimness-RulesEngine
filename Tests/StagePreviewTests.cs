using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StagePreviewMessages;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Previews;
using NUnit.Framework;

namespace FDG.Tests
{
    // #277 live decision-preview sharing: channel routing (host-capable bus broadcasts directly,
    // client bus submits for relay), relayer validation (connection ownership, size, rate), and
    // feed semantics (latest-wins per slot, local filter, clear + last-task-resolved expiry).
    [TestFixture]
    public class StagePreviewTests
    {
        private const string SLOT = "ghost";
        private const string TYPE_NAME = "TestPreview";
        private const string JSON = "{\"x\":1}";

        // ---------- Channel ----------

        [Test]
        public void Channel_HostCapableBus_BroadcastsAuthoritativeForm()
        {
            var bus = new RecordingBus();
            var channel = new PreviewChannel(bus);
            var playerID = new PlayerID(Guid.NewGuid());

            channel.PublishUpdate(playerID, SLOT, TYPE_NAME, JSON);
            channel.PublishClear(playerID);

            Assert.That(bus.Broadcasts, Has.Count.EqualTo(2));
            Assert.That(bus.Broadcasts[0], Is.EqualTo(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON)));
            Assert.That(bus.Broadcasts[1], Is.EqualTo(new StagePreviewClearMessage(playerID)));
            Assert.That(bus.SentToHost, Is.Empty, "A host-capable bus must not take the Submit path.");
        }

        [Test]
        public void Channel_ClientOnlyBus_SubmitsToHost()
        {
            var bus = new ClientOnlyBus();
            var channel = new PreviewChannel(bus);
            var playerID = new PlayerID(Guid.NewGuid());

            channel.PublishUpdate(playerID, SLOT, TYPE_NAME, JSON);
            channel.PublishClear(playerID);

            Assert.That(bus.SentToHost, Has.Count.EqualTo(2));
            Assert.That(bus.SentToHost[0], Is.EqualTo(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON)));
            Assert.That(bus.SentToHost[1], Is.EqualTo(new SubmitStagePreviewClearMessage(playerID)));
        }

        // ---------- Relayer ----------

        [Test]
        public void Relayer_ValidRemoteSubmit_RebroadcastsUntouched()
        {
            var (bus, playerID, connectionID, _) = BuildRelayerFixture();

            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON), connectionID);

            Assert.That(bus.Broadcasts, Has.Count.EqualTo(1));
            Assert.That(bus.Broadcasts[0], Is.EqualTo(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON)));
        }

        [Test]
        public void Relayer_ClearSubmit_RebroadcastsClear()
        {
            var (bus, playerID, connectionID, _) = BuildRelayerFixture();

            bus.DispatchFromConnection(new SubmitStagePreviewClearMessage(playerID), connectionID);

            Assert.That(bus.Broadcasts, Has.Count.EqualTo(1));
            Assert.That(bus.Broadcasts[0], Is.EqualTo(new StagePreviewClearMessage(playerID)));
        }

        [Test]
        public void Relayer_SpoofedPlayerID_Dropped()
        {
            // A client claiming another player's identity must not get its preview relayed.
            var (bus, _, connectionID, _) = BuildRelayerFixture();
            var someoneElse = new PlayerID(Guid.NewGuid());

            bus.DispatchFromConnection(new SubmitStagePreviewMessage(someoneElse, SLOT, TYPE_NAME, JSON), connectionID);

            Assert.That(bus.Broadcasts, Is.Empty);
        }

        [Test]
        public void Relayer_UnknownConnection_Dropped()
        {
            var (bus, playerID, _, _) = BuildRelayerFixture();
            var strangerConnection = new ConnectionID(Guid.NewGuid());

            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON), strangerConnection);

            Assert.That(bus.Broadcasts, Is.Empty);
        }

        [Test]
        public void Relayer_MalformedOrOversizedPayload_Dropped()
        {
            var (bus, playerID, connectionID, _) = BuildRelayerFixture();
            string oversized = new string('x', PreviewRelayer.MAX_PREVIEW_JSON_CHARS + 1);

            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, oversized), connectionID);
            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, "", TYPE_NAME, JSON), connectionID);
            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, null!, JSON), connectionID);
            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, null!), connectionID);

            Assert.That(bus.Broadcasts, Is.Empty);
        }

        [Test]
        public void Relayer_FloodingConnection_CappedPerSecond()
        {
            var (bus, playerID, connectionID, _) = BuildRelayerFixture();

            // A tight burst lands inside one rate window (the window is 1s of wall clock; even a
            // slow CI machine dispatches these in far less).
            for (int i = 0; i < PreviewRelayer.MAX_MESSAGES_PER_SECOND_PER_CONNECTION * 3; i++)
            {
                bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON), connectionID);
            }

            Assert.That(bus.Broadcasts, Has.Count.LessThanOrEqualTo(PreviewRelayer.MAX_MESSAGES_PER_SECOND_PER_CONNECTION));
            Assert.That(bus.Broadcasts, Is.Not.Empty, "Messages under the cap must still relay.");
        }

        // ---------- Feed ----------

        [Test]
        public void Feed_Update_AppearsInSnapshot_AndBumpsVersion()
        {
            var bus = new RecordingBus();
            var feed = new PreviewFeed(bus, new List<PlayerID>());
            var playerID = new PlayerID(Guid.NewGuid());
            int versionBefore = feed.Version;

            bus.DispatchLocal(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON));

            Assert.That(feed.Version, Is.GreaterThan(versionBefore));
            IReadOnlyList<PreviewEntry> snapshot = feed.GetSnapshot();
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0], Is.EqualTo(new PreviewEntry(playerID, SLOT, TYPE_NAME, JSON)));
        }

        [Test]
        public void Feed_LatestWinsPerSlot_SeparateSlotsCoexist()
        {
            var bus = new RecordingBus();
            var feed = new PreviewFeed(bus, new List<PlayerID>());
            var playerID = new PlayerID(Guid.NewGuid());

            bus.DispatchLocal(new StagePreviewMessage(playerID, "ghost", TYPE_NAME, "{\"x\":1}"));
            bus.DispatchLocal(new StagePreviewMessage(playerID, "ghost", TYPE_NAME, "{\"x\":2}"));
            bus.DispatchLocal(new StagePreviewMessage(playerID, "base", TYPE_NAME, "{\"b\":1}"));

            IReadOnlyList<PreviewEntry> snapshot = feed.GetSnapshot();
            Assert.That(snapshot, Has.Count.EqualTo(2));
            Assert.That(snapshot.Single(e => e.Slot == "ghost").PreviewJson, Is.EqualTo("{\"x\":2}"));
            Assert.That(snapshot.Single(e => e.Slot == "base").PreviewJson, Is.EqualTo("{\"b\":1}"));
        }

        [Test]
        public void Feed_LocalPlayer_Filtered()
        {
            // Local players' previews draw live through their own overlay; the broadcast loopback
            // must not double-draw them. The list reference is LIVE: a player added after feed
            // construction (the host's AddLocalPlayerID flow) is filtered too.
            var bus = new RecordingBus();
            var localPlayers = new List<PlayerID>();
            var feed = new PreviewFeed(bus, localPlayers);
            var playerID = new PlayerID(Guid.NewGuid());
            localPlayers.Add(playerID);

            int versionBefore = feed.Version;
            bus.DispatchLocal(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON));

            Assert.That(feed.GetSnapshot(), Is.Empty);
            Assert.That(feed.Version, Is.EqualTo(versionBefore), "A filtered update must not bump the version.");
        }

        [Test]
        public void Feed_ClearMessage_RemovesAllPlayerSlots()
        {
            var bus = new RecordingBus();
            var feed = new PreviewFeed(bus, new List<PlayerID>());
            var playerA = new PlayerID(Guid.NewGuid());
            var playerB = new PlayerID(Guid.NewGuid());

            bus.DispatchLocal(new StagePreviewMessage(playerA, "ghost", TYPE_NAME, JSON));
            bus.DispatchLocal(new StagePreviewMessage(playerA, "base", TYPE_NAME, JSON));
            bus.DispatchLocal(new StagePreviewMessage(playerB, "ghost", TYPE_NAME, JSON));

            bus.DispatchLocal(new StagePreviewClearMessage(playerA));

            IReadOnlyList<PreviewEntry> snapshot = feed.GetSnapshot();
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0].SourcePlayerID, Is.EqualTo(playerB));
        }

        [Test]
        public void Feed_LastTaskResolved_ExpiresPreviews()
        {
            // The crash/disconnect safety net: the disconnect path broadcasts a resolved
            // notification per failed task (#076), so a publisher that never sent its clear still
            // gets cleaned up when its last outstanding request resolves.
            var bus = new RecordingBus();
            var feed = new PreviewFeed(bus, new List<PlayerID>());
            var playerID = new PlayerID(Guid.NewGuid());
            var slotInfo = new PlayerSlotInfo(playerID, 0, 0, "Bob", true);
            var taskID = new TaskID(Guid.NewGuid());

            bus.DispatchLocal(new StageTaskNotifyAwaitingMessage(taskID, slotInfo, "Move"));
            bus.DispatchLocal(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON));
            bus.DispatchLocal(new StageTaskNotifyResolvedMessage(taskID));

            Assert.That(feed.GetSnapshot(), Is.Empty);
        }

        [Test]
        public void Feed_TaskResolved_KeepsPreviewWhileAnotherTaskOutstanding()
        {
            var bus = new RecordingBus();
            var feed = new PreviewFeed(bus, new List<PlayerID>());
            var playerID = new PlayerID(Guid.NewGuid());
            var slotInfo = new PlayerSlotInfo(playerID, 0, 0, "Bob", true);
            var task1 = new TaskID(Guid.NewGuid());
            var task2 = new TaskID(Guid.NewGuid());

            bus.DispatchLocal(new StageTaskNotifyAwaitingMessage(task1, slotInfo, "Move"));
            bus.DispatchLocal(new StageTaskNotifyAwaitingMessage(task2, slotInfo, "Wounds"));
            bus.DispatchLocal(new StagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON));

            bus.DispatchLocal(new StageTaskNotifyResolvedMessage(task1));
            Assert.That(feed.GetSnapshot(), Has.Count.EqualTo(1),
                "Preview must survive while the player still has a request outstanding.");

            bus.DispatchLocal(new StageTaskNotifyResolvedMessage(task2));
            Assert.That(feed.GetSnapshot(), Is.Empty);
        }

        [Test]
        public void EndToEnd_RemoteSubmitReachesFeedThroughRelay()
        {
            // Client-shaped submit -> relayer validation -> broadcast -> feed snapshot, on one bus
            // (the RecordingBus loops broadcasts back to handlers like the real host bus does).
            var (bus, playerID, connectionID, _) = BuildRelayerFixture();
            var feed = new PreviewFeed(bus, new List<PlayerID>());

            bus.DispatchFromConnection(new SubmitStagePreviewMessage(playerID, SLOT, TYPE_NAME, JSON), connectionID);

            IReadOnlyList<PreviewEntry> snapshot = feed.GetSnapshot();
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0], Is.EqualTo(new PreviewEntry(playerID, SLOT, TYPE_NAME, JSON)));
        }

        // ---------- Fixtures & doubles ----------

        /// <summary>
        /// A relayer listening on a RecordingBus, with one remote player on a known connection.
        /// The relayer is reachable only through the bus (kept alive by its registrations).
        /// </summary>
        private static (RecordingBus Bus, PlayerID PlayerID, ConnectionID ConnectionID, PreviewRelayer Relayer)
            BuildRelayerFixture()
        {
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            var bus = new RecordingBus();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());

            PlayerSlot slot = new PlayerSlot(0, 0, playerID, null, gameDataStore);
            slot.AssignPlayerController(new NetworkPlayerController("Remote", playerID, connectionID, bus, gameDataStore));
            var playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });

            var relayer = new PreviewRelayer(bus, playerSlotManager, new EmptyTextOutput());
            return (bus, playerID, connectionID, relayer);
        }

        /// <summary>
        /// Host-capable bus double: records broadcasts and host-sends, dispatches to registered
        /// handlers with a caller-chosen source connection (which the shared InProcessBus cannot
        /// do), and loops SendCommandToAllAsync back through local handlers like the real
        /// MessageBusHost_Networked.
        /// </summary>
        private class RecordingBus : IMessageBusHost, IMessageBusClient
        {
            public List<object> Broadcasts { get; } = new List<object>();
            public List<object> SentToHost { get; } = new List<object>();

            private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

#pragma warning disable CS0067 // Required by IMessageBusHost; never raised by this double.
            public event Action<ConnectionID>? OnClientDisconnected;
#pragma warning restore CS0067

            public void RegisterForMessageEvent<T>(Action<T> handler) => Add(typeof(T), handler);
            public void DeregisterForMessageEvent<T>(Action<T> handler) => Remove(typeof(T), handler);
            public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Add(typeof(T), handler);
            public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Remove(typeof(T), handler);

            public Task SendCommandToAllAsync<TMessage>(TMessage message)
            {
                Broadcasts.Add(message!);
                DispatchLocal(message); // The real host bus dispatches broadcasts in-process too.
                return Task.CompletedTask;
            }

            public Task SendCommandToHostAsync<TMessage>(TMessage message)
            {
                SentToHost.Add(message!);
                return Task.CompletedTask;
            }

            public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID) => Task.CompletedTask;
            public Task SendCommandToLocalAsync<TMessage>(TMessage message) { DispatchLocal(message); return Task.CompletedTask; }

            /// <summary>Simulates a message arriving over the wire on a specific connection.</summary>
            public void DispatchFromConnection<T>(T message, ConnectionID connectionID)
            {
                if (message == null || _handlers.TryGetValue(typeof(T), out var list) == false) return;
                foreach (Delegate handler in list.ToList())
                {
                    if (handler is Action<T> plain) plain(message);
                    else if (handler is Action<T, ConnectionID> withConnection) withConnection(message, connectionID);
                }
            }

            /// <summary>Simulates a locally-originated dispatch (no connection - skips connection-aware handlers).</summary>
            public void DispatchLocal<T>(T message)
            {
                if (message == null || _handlers.TryGetValue(typeof(T), out var list) == false) return;
                foreach (Delegate handler in list.ToList())
                {
                    if (handler is Action<T> plain) plain(message);
                }
            }

            public void Dispose() { }

            private void Add(Type type, Delegate handler)
            {
                if (_handlers.TryGetValue(type, out var list) == false) _handlers[type] = list = new List<Delegate>();
                list.Add(handler);
            }

            private void Remove(Type type, Delegate handler)
            {
                if (_handlers.TryGetValue(type, out var list)) list.Remove(handler);
            }
        }

        /// <summary>Client-only bus double (does NOT implement IMessageBusHost), for the Submit routing path.</summary>
        private class ClientOnlyBus : IMessageBusClient
        {
            public List<object> SentToHost { get; } = new List<object>();

            public void RegisterForMessageEvent<T>(Action<T> handler) { }
            public void DeregisterForMessageEvent<T>(Action<T> handler) { }

            public Task SendCommandToHostAsync<TMessage>(TMessage message)
            {
                SentToHost.Add(message!);
                return Task.CompletedTask;
            }

            public void Dispose() { }
        }
    }
}
