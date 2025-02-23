using FDG.StageResolution;
using NUnit.Framework;


namespace FDG.Tests
{
    [TestFixture]
    public class StageResolutionTests
    {
        [Test]
        public void NotifyTaskRequested_AddsTaskToOutstandingTasks()
        {
            OutstandingTaskLister taskLister = new OutstandingTaskLister(); 

            var taskID = new TaskID(Guid.NewGuid());
            var playerID = new PlayerID(Guid.NewGuid());
            var taskName = "Test Task";

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            taskLister.NotifyTaskRequested(playerID, taskID, taskName);

            Assert.That(taskList.Last().Count, Is.EqualTo(1));
            Assert.That(taskList.Last().First().PlayerID, Is.EqualTo(playerID));
            Assert.That(taskList.Last().First().TaskName, Is.EqualTo(taskName));
        }

        [Test]
        public void NotifyTaskResolved_RemovesTaskFromOutstandingTasks()
        {
            OutstandingTaskLister taskLister = new OutstandingTaskLister();

            var taskID = new TaskID(Guid.NewGuid());
            var playerID = new PlayerID(Guid.NewGuid());
            var taskName = "Test Task";

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            taskLister.NotifyTaskRequested(playerID, taskID, taskName);
            taskLister.NotifyTaskResolved(taskID);

            Assert.That(taskList.Last().Count, Is.EqualTo(0));
        }
    }
}
