using FDG.Data;
using FDG.Network.Messages.StageRequestMessages;
using FDG.Players;
using FDG.StageResolution;
using NUnit.Framework;
using static FDG.Tests.RequestSystemTests;


namespace FDG.Tests
{
    [TestFixture]
    public class StageResolutionTests
    {

        private MockMessageBusHost _mockMessageBusHost;
        private GameDataStore _gameDataStore;
        private PlayerID _playerID;
        private PlayerSlotInfo _playerSlotInfo;
        private DataBinding<PlayerSlotInfo> _playerBinding;

        [SetUp]
        public void SetUp()
        {
            _mockMessageBusHost = new MockMessageBusHost();

            _gameDataStore = new GameDataStore.GameDataStoreBuilder()
                .RegisterType<PlayerSlotInfo>(1)
                .Build();

            _playerID = new PlayerID(Guid.NewGuid());
            _playerSlotInfo = new PlayerSlotInfo(_playerID, 0, 0, "Bob", true);

            DataReference playerInfoReference = _gameDataStore.Create<PlayerSlotInfo>(_playerSlotInfo);
            _playerBinding = _gameDataStore.GetDataBinding<PlayerSlotInfo>(playerInfoReference);
        }

        [Test]
        public void NotifyTaskRequested_AddsTaskToOutstandingTasks()
        {
            OutstandingTaskLister taskLister = new OutstandingTaskLister(_mockMessageBusHost); 

            var taskID = new TaskID(Guid.NewGuid());
            var taskName = "Test Task";

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            //taskLister.NotifyTaskRequested(_playerID, taskID, taskName);
            StageTaskNotifyAwaitingMessage notifyMessage = new StageTaskNotifyAwaitingMessage(taskID, _playerBinding, taskName);
            _mockMessageBusHost.SimulateMessageReceived(notifyMessage);

            Assert.That(taskList.Last().Count, Is.EqualTo(1));
            Assert.That(taskList.Last().First().PlayerInfo.PlayerID, Is.EqualTo(_playerID));
            Assert.That(taskList.Last().First().TaskName, Is.EqualTo(taskName));
        }

        [Test]
        public void NotifyTaskResolved_RemovesTaskFromOutstandingTasks()
        {
            OutstandingTaskLister taskLister = new OutstandingTaskLister(_mockMessageBusHost);

            var taskID = new TaskID(Guid.NewGuid());
            var playerID = new PlayerID(Guid.NewGuid());
            var taskName = "Test Task";

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            //taskLister.NotifyTaskRequested(playerID, taskID, taskName);

            StageTaskNotifyAwaitingMessage notifyMessage = new StageTaskNotifyAwaitingMessage(taskID, _playerBinding, taskName);
            _mockMessageBusHost.SimulateMessageReceived(notifyMessage);

            //taskLister.NotifyTaskResolved(taskID);
            StageTaskNotifyResolvedMessage resolvedMessage = new StageTaskNotifyResolvedMessage(taskID);
            _mockMessageBusHost.SimulateMessageReceived(resolvedMessage);

            Assert.That(taskList.Last().Count, Is.EqualTo(0));
        }
    }
}
