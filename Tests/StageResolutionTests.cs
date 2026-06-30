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
        private PlayerID _playerID;
        private PlayerSlotInfo _playerSlotInfo;

        [SetUp]
        public void SetUp()
        {
            _mockMessageBusHost = new MockMessageBusHost();

            _playerID = new PlayerID(Guid.NewGuid());
            // The awaiting message now carries a PlayerSlotInfo value snapshot (no live binding), so the
            // lister no longer needs a backing GameDataStore entry (#088).
            _playerSlotInfo = new PlayerSlotInfo(_playerID, 0, 0, "Bob", true);
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
            StageTaskNotifyAwaitingMessage notifyMessage = new StageTaskNotifyAwaitingMessage(taskID, _playerSlotInfo, taskName);
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

            StageTaskNotifyAwaitingMessage notifyMessage = new StageTaskNotifyAwaitingMessage(taskID, _playerSlotInfo, taskName);
            _mockMessageBusHost.SimulateMessageReceived(notifyMessage);

            //taskLister.NotifyTaskResolved(taskID);
            StageTaskNotifyResolvedMessage resolvedMessage = new StageTaskNotifyResolvedMessage(taskID);
            _mockMessageBusHost.SimulateMessageReceived(resolvedMessage);

            Assert.That(taskList.Last().Count, Is.EqualTo(0));
        }
    }
}
