using FDG.StageResolution;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            var mockTaskRequest = new Mock<IStageTaskRequest>();
            mockTaskRequest.Setup(t => t.TaskID).Returns(taskID);
            mockTaskRequest.Setup(t => t.TargetPlayerID).Returns(playerID);
            mockTaskRequest.Setup(t => t.TaskName).Returns(taskName);

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            taskLister.NotifyTaskRequested(mockTaskRequest.Object);

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

            var mockTaskRequest = new Mock<IStageTaskRequest>();
            mockTaskRequest.Setup(t => t.TaskID).Returns(taskID);
            mockTaskRequest.Setup(t => t.TargetPlayerID).Returns(playerID);
            mockTaskRequest.Setup(t => t.TaskName).Returns(taskName);

            var taskList = new List<IReadOnlyCollection<OutstandingTaskInfo>>();
            taskLister.OutstandingTasks.Subscribe(taskList.Add);

            taskLister.NotifyTaskRequested(mockTaskRequest.Object);
            taskLister.NotifyTaskResolved(taskID);

            Assert.That(taskList.Last().Count, Is.EqualTo(0));
        }
    }
}
