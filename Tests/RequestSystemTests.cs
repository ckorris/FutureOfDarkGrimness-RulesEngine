using FDG.Data;
using FDG.MessageBus;
using FDG.Network.Connection;
using FDG.Network.Messages.StageRequestMessages;
using FDG.StageResolution;
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
            var connectionID = new ConnectionID(Guid.NewGuid());
            var mockCommandDispatcher = new MockMessageBusHost();
            var gameDataStore = new GameDataStore.GameDataStoreBuilder().Build();
            var sender = new RequestMessageSender(playerID, connectionID, mockCommandDispatcher, gameDataStore);

            var request = new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task");

            // Act
            var resolveTask = sender.ResolveRequestOverNetwork<TestRequest, string>(request);
            
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
            var connectionID = new ConnectionID(Guid.NewGuid());
            var mockCommandDispatcher = new MockMessageBusHost();
            var gameDataStore = new GameDataStore.GameDataStoreBuilder().Build();
            var sender = new RequestMessageSender(playerID, connectionID, mockCommandDispatcher, gameDataStore);

            var request = new TestRequest(playerID, new TaskID(Guid.NewGuid()), "Test Task");

            // Act
            var resolveTask = sender.ResolveRequestOverNetwork<TestRequest, string>(request);
            
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
            // Arrange
            var taskLister = new OutstandingTaskLister();
            var playerID = new PlayerID(Guid.NewGuid());
            var taskID1 = new TaskID(Guid.NewGuid());
            var taskID2 = new TaskID(Guid.NewGuid());

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            // Act
            taskLister.NotifyTaskRequested(playerID, taskID1, "Task 1");
            taskLister.NotifyTaskRequested(playerID, taskID2, "Task 2");

            // Assert
            Assert.That(taskList.Last().Count, Is.EqualTo(2));
            Assert.That(taskList.Last().Any(t => t.PlayerID == playerID && t.TaskName == "Task 1"), Is.True);
            Assert.That(taskList.Last().Any(t => t.PlayerID == playerID && t.TaskName == "Task 2"), Is.True);
        }

        // Mock command dispatcher for testing network requests
        private class MockMessageBusHost : IMessageBusHost
        {
            private readonly Dictionary<Type, Action<object, ConnectionID>> _messageHandlers = new();
            public StageTaskRequestMessage? LastRequestMessage { get; private set; }

            public void RegisterForMessageEvent<T>(Action<T, ConnectionID> handler)
            {
                _messageHandlers[typeof(T)] = (message, connectionID) => handler((T)message, connectionID);
            }

            public void DeregisterForMessageEvent<T>(Action<T, ConnectionID> handler)
            {
                _messageHandlers.Remove(typeof(T));
            }

            public Task SendCommandToAllAsync<TMessage>(TMessage command)
            {
                if (command is StageTaskRequestMessage requestMessage)
                {
                    LastRequestMessage = requestMessage;
                }
                // Do nothing in mock
                return Task.CompletedTask;
            }

            public Task SendCommandToSingleAsync<TMessage>(TMessage command, ConnectionID connectionID)
            {
                if (command is StageTaskRequestMessage requestMessage)
                {
                    LastRequestMessage = requestMessage;
                }
                // Do nothing in mock
                return Task.CompletedTask;
            }

            public void SimulateMessageReceived<T>(T message)
            {
                if (_messageHandlers.TryGetValue(typeof(T), out var handler))
                {
                    handler(message, new ConnectionID(Guid.NewGuid()));
                }
            }
        }
    }
} 