using FDG.Data;
using FDG.GameModel;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace FDG.Tests
{
    [TestFixture]
    public class RequestSystemTests
    {
        // Test request and reply types
        private class TestRequest : IStageTaskRequest<string>
        {
            public PlayerID TargetPlayerID { get; }
            public TaskID TaskID { get; }
            public string TaskName { get; }

            public TestRequest(PlayerID targetPlayerID, TaskID taskID, string taskName)
            {
                TargetPlayerID = targetPlayerID;
                TaskID = taskID;
                TaskName = taskName;
            }

            public Task<string> Resolve(string resolution)
            {
                return Task.FromResult(resolution);
            }
        }

        private class TestResolver : IStageResolver<TestRequest, string>
        {
            private readonly string _expectedResponse;

            public TestResolver(string expectedResponse)
            {
                _expectedResponse = expectedResponse;
            }

            public Task<string> Resolve(TestRequest context)
            {
                return Task.FromResult(_expectedResponse);
            }
        }

        [Test]
        public void RegisterAndResolveRequest_Success()
        {
            // Arrange
            var registry = new StageResolverRegistry();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());
            var expectedResponse = "Test Response";
            var resolver = new TestResolver(expectedResponse);
            var request = new TestRequest(playerID, taskID, "Test Task");

            // Act
            registry.RegisterResolver<TestRequest, string>(resolver);
            var result = registry.ResolveRequest<TestRequest, string>(request).Result;

            // Assert
            Assert.That(result, Is.EqualTo(expectedResponse));
        }

        [Test]
        public void RegisterDuplicateResolver_ThrowsException()
        {
            // Arrange
            var registry = new StageResolverRegistry();
            var resolver1 = new TestResolver("Response 1");
            var resolver2 = new TestResolver("Response 2");

            // Act & Assert
            registry.RegisterResolver<TestRequest, string>(resolver1);
            Assert.Throws<StageResolverRegistry.ResolverAlreadyAddedException>(() => 
                registry.RegisterResolver<TestRequest, string>(resolver2));
        }

        [Test]
        public void ResolveUnregisteredRequest_ThrowsException()
        {
            // Arrange
            var registry = new StageResolverRegistry();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID = new TaskID(Guid.NewGuid());
            var request = new TestRequest(playerID, taskID, "Test Task");

            // Act & Assert
            Assert.Throws<StageHandlerRegistry.MissingHandlerException>(() => 
                registry.ResolveRequest<TestRequest, string>(request).Wait());
        }

        [Test]
        public async Task NetworkRequestMessageSender_SendsAndReceivesResponse()
        {
            // Arrange
            var playerID = new PlayerID(Guid.NewGuid());
            var mockCommandDispatcher = new MockMessageBusHost();
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            PlayerSlot slot = new PlayerSlot(1, 1, playerID, null, gameDataStore);
            PlayerSlotManager playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });
            
            var sender = new RequestMessageSender(mockCommandDispatcher, gameDataStore, playerSlotManager, new EmptyTextOutput());

            var request = new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task");

            // Act
            var resolveTask = sender.RequestDecision<TestRequest, string>(request);
            
            // Get the actual task ID used in the request
            var actualTaskID = mockCommandDispatcher.LastRequestMessage?.TaskID 
                ?? throw new InvalidOperationException("No request message was sent");
            
            // Simulate receiving a response with the actual task ID
            var responseMessage = new StageTaskReplyMessage(
                playerID, 
                actualTaskID, 
                typeof(string).FullName ?? throw new InvalidOperationException("String type has no full name"),
                "\"Test Response\"");
            mockCommandDispatcher.SimulateMessageReceived(responseMessage);

            // Assert
            var result = await resolveTask;
            Assert.That(result, Is.EqualTo("Test Response"));
        }

        [Test]
        public async Task NetworkRequestMessageSender_HandlesErrorResponse()
        {
            // Arrange
            var playerID = new PlayerID(Guid.NewGuid());
            var mockCommandDispatcher = new MockMessageBusHost();
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            PlayerSlot slot = new PlayerSlot(1, 1, playerID, null, gameDataStore);
            PlayerSlotManager playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });

            var sender = new RequestMessageSender(mockCommandDispatcher, gameDataStore, playerSlotManager, new EmptyTextOutput());

            var request = new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task");

            // Act
            var resolveTask = sender.RequestDecision<TestRequest, string>(request);
            
            // Get the actual task ID used in the request
            var actualTaskID = mockCommandDispatcher.LastRequestMessage?.TaskID 
                ?? throw new InvalidOperationException("No request message was sent");
            
            // Simulate receiving an error with the actual task ID
            var errorMessage = new StageTaskRequestErrorMessage(playerID, actualTaskID, "Test Error");
            mockCommandDispatcher.SimulateMessageReceived(errorMessage);

            // Assert
            var exception = Assert.ThrowsAsync<RequestMessageSender.NetworkedRequestFailedException>(
                async () => await resolveTask);
            Assert.That(exception.Message, Does.Contain("Test Error"));
        }

        [Test]
        public void OutstandingTaskLister_TracksMultipleTasks()
        {
            var mockCommandDispatcher = new MockMessageBusHost();

            var playerID = new PlayerID(Guid.NewGuid());
            PlayerSlotInfo slotInfo = new PlayerSlotInfo(playerID, 0, 0, "Bob", true);

            var taskLister = new OutstandingTaskLister(mockCommandDispatcher);
            
            var taskID1 = new TaskID(Guid.NewGuid());
            var taskID2 = new TaskID(Guid.NewGuid());

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            StageTaskNotifyAwaitingMessage notifyMessage1 = new StageTaskNotifyAwaitingMessage(taskID1, slotInfo, "Task 1");
            mockCommandDispatcher.SimulateMessageReceived(notifyMessage1);


            StageTaskNotifyAwaitingMessage notifyMessage2 = new StageTaskNotifyAwaitingMessage(taskID2, slotInfo, "Task 2");
            mockCommandDispatcher.SimulateMessageReceived(notifyMessage2);
            // Act
            //taskLister.NotifyTaskRequested(playerID, taskID1, "Task 1");
            //taskLister.NotifyTaskRequested(playerID, taskID2, "Task 2");

            // Assert
            Assert.That(taskList.Last().Count, Is.EqualTo(2));
            Assert.That(taskList.Last().Any(t => t.PlayerInfo.PlayerID == playerID && t.TaskName == "Task 1"), Is.True);
            Assert.That(taskList.Last().Any(t => t.PlayerInfo.PlayerID == playerID && t.TaskName == "Task 2"), Is.True);
        }

        [Test]
        public void LocalPlayerIDs_ExposedOnBothGameFlavors()
        {
            // #322: the front end filters the outstanding-task HUD line to non-local players, so both
            // game flavors must expose who is driven from this process.
            var hostID = new PlayerID(Guid.NewGuid());
            var hotseatID = new PlayerID(Guid.NewGuid());
            var localGame = new FDGGame_AsLocal(GameDataStore.GameDataStoreBuilder.GetDefault(), new InProcessBus());
            localGame.AddLocalPlayerID(hostID);
            localGame.AddLocalPlayerID(hotseatID);
            Assert.That(localGame.LocalPlayerIDs, Is.EquivalentTo(new[] { hostID, hotseatID }));

            var clientID = new PlayerID(Guid.NewGuid());
            var clientGame = new FDGGame_AsClient(GameDataStore.GameDataStoreBuilder.GetDefault(),
                new InProcessBus(), clientID);
            Assert.That(clientGame.LocalPlayerIDs, Is.EquivalentTo(new[] { clientID }));
        }

        [Test]
        public void RequestDecision_AwaitingNotification_CarriesDisplayName()
        {
            // #322: the broadcast "waiting on" notification carries DisplayName - the game wording
            // requests override for the HUD - falling back to TaskName when nothing is overridden.
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            var mock = new MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            PlayerSlot slot = new PlayerSlot(0, 0, playerID, null, gameDataStore);
            PlayerSlotManager playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });
            var sender = new RequestMessageSender(mock, gameDataStore, playerSlotManager, new EmptyTextOutput());

            _ = sender.RequestDecision<SelectionRequest<UnitData>, DataBinding<UnitData>>(
                new SelectionRequest<UnitData>(playerID, "Choose Unit to Deploy",
                    Array.Empty<SelectionRequest<UnitData>.ValidOption>(),
                    Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                    allowCancel: false, displayName: "Choosing Unit to Deploy"));
            Assert.That(mock.LastAwaitingMessage?.UserFriendlyTaskName, Is.EqualTo("Choosing Unit to Deploy"));

            _ = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));
            Assert.That(mock.LastAwaitingMessage?.UserFriendlyTaskName, Is.EqualTo("Test Task"));
        }

        [Test]
        public void RequestDecision_RemotePlayer_RoutesToThatConnectionOnly()
        {
            // A player on a network connection should receive their decision request on that connection
            // alone — not broadcast to every client (#088).
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            var mock = new MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());

            PlayerSlot slot = new PlayerSlot(0, 0, playerID, null, gameDataStore);
            NetworkPlayerController controller =
                new NetworkPlayerController("Remote", playerID, connectionID, mock, gameDataStore);
            slot.AssignPlayerController(controller);

            PlayerSlotManager playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });
            var sender = new RequestMessageSender(mock, gameDataStore, playerSlotManager, new EmptyTextOutput());

            _ = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));

            Assert.That(mock.LastRequestWasBroadcast, Is.False, "Request must not be broadcast to all clients.");
            Assert.That(mock.LastRequestWasLocal, Is.False, "A remote player's request must route over the network, not local dispatch.");
            Assert.That(mock.LastRequestSingleConnection, Is.EqualTo(connectionID));
        }

        [Test]
        public void RequestDecision_LocalPlayer_RoutesInProcessOnly()
        {
            // A local (in-process) player has no connection, so their request should dispatch locally with
            // no network broadcast (#088).
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            var mock = new MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());

            // No controller assigned → not a NetworkPlayerController → treated as a local player.
            PlayerSlot slot = new PlayerSlot(0, 0, playerID, null, gameDataStore);
            PlayerSlotManager playerSlotManager = new PlayerSlotManager(new PlayerSlot[] { slot });
            var sender = new RequestMessageSender(mock, gameDataStore, playerSlotManager, new EmptyTextOutput());

            _ = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));

            Assert.That(mock.LastRequestWasBroadcast, Is.False, "Request must not be broadcast to all clients.");
            Assert.That(mock.LastRequestWasLocal, Is.True);
            Assert.That(mock.LastRequestSingleConnection, Is.Null);
        }

        // #451 (replay decision D2): once a decision is over, every client is told what it was - the request as
        // sent and the reply as received - so it can record other players' decisions. One test per way a task
        // ends: answered, resolver error, disconnect.
        private static (MockMessageBusHost Bus, RequestMessageSender Sender, PlayerID PlayerID, ConnectionID ConnectionID)
            RemotePlayerSender()
        {
            var gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();
            var mock = new MockMessageBusHost();
            var playerID = new PlayerID(Guid.NewGuid());
            var connectionID = new ConnectionID(Guid.NewGuid());

            PlayerSlot slot = new PlayerSlot(0, 0, playerID, null, gameDataStore);
            slot.AssignPlayerController(new NetworkPlayerController("Remote", playerID, connectionID, mock, gameDataStore));
            var sender = new RequestMessageSender(mock, gameDataStore, new PlayerSlotManager(new[] { slot }), new EmptyTextOutput());
            return (mock, sender, playerID, connectionID);
        }

        [Test]
        public async Task Reply_BroadcastsTheResolvedDecision_RequestAndReply()
        {
            var (mock, sender, playerID, _) = RemotePlayerSender();
            Task<string> decision = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));
            StageTaskRequestMessage request = mock.LastRequestMessage!;

            // The replier's claimed PlayerID is not what the notice reports: the host's own record of the target is.
            mock.SimulateMessageReceived(new StageTaskReplyMessage(new PlayerID(Guid.NewGuid()), request.TaskID,
                request.ReplyFullTypeName, "\"Fire!\""));

            Assert.That(await decision, Is.EqualTo("Fire!"));
            Assert.That(mock.LastSentWasBroadcast, Is.True, "the resolved notice goes to every client");
            Assert.That(mock.LastSentCommand, Is.EqualTo(new StageTaskNotifyResolvedMessage(request.TaskID, playerID,
                request.RequestFullTypeName, request.ReplyFullTypeName, "Test Task", request.RequestJson,
                ReplyJson: "\"Fire!\"", ErrorMessage: null)));
        }

        [Test]
        public void ResolverError_BroadcastsTheResolvedNotice_WithTheError()
        {
            var (mock, sender, playerID, _) = RemotePlayerSender();
            Task<string> decision = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));
            StageTaskRequestMessage request = mock.LastRequestMessage!;

            mock.SimulateMessageReceived(new StageTaskRequestErrorMessage(playerID, request.TaskID, "resolver blew up"));

            Assert.That(async () => await decision, Throws.InstanceOf<RequestMessageSender.NetworkedRequestFailedException>());
            Assert.That(mock.LastSentWasBroadcast, Is.True, "an error ends the task like a reply: every client is told");
            Assert.That(mock.LastSentCommand, Is.EqualTo(new StageTaskNotifyResolvedMessage(request.TaskID, playerID,
                request.RequestFullTypeName, request.ReplyFullTypeName, "Test Task", request.RequestJson,
                ReplyJson: null, ErrorMessage: "resolver blew up")));
        }

        [Test]
        public void Disconnect_BroadcastsTheResolvedNotice_WithTheReason()
        {
            var (mock, sender, playerID, connectionID) = RemotePlayerSender();
            Task<string> decision = sender.RequestDecision<TestRequest, string>(
                new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task"));
            StageTaskRequestMessage request = mock.LastRequestMessage!;

            mock.SimulateClientDisconnected(connectionID);

            Assert.That(async () => await decision, Throws.InstanceOf<RequestMessageSender.PlayerDisconnectedException>());
            Assert.That(mock.LastSentWasBroadcast, Is.True);
            Assert.That(mock.LastSentCommand, Is.EqualTo(new StageTaskNotifyResolvedMessage(request.TaskID, playerID,
                request.RequestFullTypeName, request.ReplyFullTypeName, "Test Task", request.RequestJson,
                ReplyJson: null, ErrorMessage: "Player disconnected.")));
        }

        // Mock command dispatcher for testing network requests
        public class MockMessageBusHost : IMessageBusHost, IMessageBusClient
        {
            private readonly Dictionary<Type, Action<object>> _messageHandlers = new();
            public StageTaskRequestMessage? LastRequestMessage { get; private set; }
            public StageTaskNotifyAwaitingMessage? LastAwaitingMessage { get; private set; }

            // How the last request message was routed (#088): the connection a single-send targeted, or
            // a flag that it went out in-process to a local player. Null until a request is sent.
            public ConnectionID? LastRequestSingleConnection { get; private set; }
            public bool LastRequestWasLocal { get; private set; }
            public bool LastRequestWasBroadcast { get; private set; }

            // Routing of the last send of ANY message type (not just requests): what was sent, to which
            // connection (null for broadcast/local), and whether it was a broadcast.
            public object? LastSentCommand { get; private set; }
            public ConnectionID? LastSentConnection { get; private set; }
            public bool LastSentWasBroadcast { get; private set; }

            public event Action<ConnectionID>? OnClientDisconnected;

            public void SimulateClientDisconnected(ConnectionID connectionID) => OnClientDisconnected?.Invoke(connectionID);

            public void RegisterForMessageEvent<T>(Action<T> handler)
            {
                _messageHandlers[typeof(T)] = (message) => handler((T)message);
            }

            public void DeregisterForMessageEvent<T>(Action<T> handler)
            {
                _messageHandlers.Remove(typeof(T));
            }

            public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler)
            {
                _messageHandlers[typeof(T)] = (message) => handler((T)message, ConnectionID.Host);
            }

            public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler)
            {
                _messageHandlers.Remove(typeof(T));
            }

            public Task SendCommandToAllAsync<TMessage>(TMessage command)
            {
                LastSentCommand = command; LastSentConnection = null; LastSentWasBroadcast = true;
                if (command is StageTaskNotifyAwaitingMessage awaitingMessage)
                {
                    LastAwaitingMessage = awaitingMessage;
                }
                if (command is StageTaskRequestMessage requestMessage)
                {
                    LastRequestMessage = requestMessage;
                    LastRequestSingleConnection = null;
                    LastRequestWasLocal = false;
                    LastRequestWasBroadcast = true;
                }
                // Do nothing in mock
                return Task.CompletedTask;
            }

            public Task SendCommandToSingleAsync<TMessage>(TMessage command, ConnectionID connectionID)
            {
                LastSentCommand = command; LastSentConnection = connectionID; LastSentWasBroadcast = false;
                if (command is StageTaskRequestMessage requestMessage)
                {
                    LastRequestMessage = requestMessage;
                    LastRequestSingleConnection = connectionID;
                    LastRequestWasLocal = false;
                    LastRequestWasBroadcast = false;
                }
                // Do nothing in mock
                return Task.CompletedTask;
            }

            public Task SendCommandToLocalAsync<TMessage>(TMessage command)
            {
                LastSentCommand = command; LastSentConnection = null; LastSentWasBroadcast = false;
                if (command is StageTaskRequestMessage requestMessage)
                {
                    LastRequestMessage = requestMessage;
                    LastRequestSingleConnection = null;
                    LastRequestWasLocal = true;
                    LastRequestWasBroadcast = false;
                }
                // Do nothing in mock
                return Task.CompletedTask;
            }

            public void SimulateMessageReceived<T>(T message)
            {
                if (_messageHandlers.TryGetValue(typeof(T), out var handler))
                {
                    handler(message);
                }
            }

            public void Dispose() { }

            public Task SendCommandToHostAsync<TMessage>(TMessage message)
            {
                throw new NotImplementedException();
            }
        }
    }
} 