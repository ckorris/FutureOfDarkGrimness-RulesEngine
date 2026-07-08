using System.Collections.Generic;
using FDG.Players;
using FDG.TextInterface;
using NUnit.Framework;

namespace FDG.Tests
{
    // The Debug log category rides the same relay chain as normal log lines, carried by an isDebug flag
    // (ITextOutput.LogDebug -> IPlayerTextRelayer -> IPlayerController -> ILogMessageUI). These tests pin
    // each seam: the flag is set by LogDebug, forwarded by the relayer, and lands on DisplayDebugMessage;
    // and the default interface members keep sinks/controllers that don't distinguish categories working.
    [TestFixture]
    public class LogDebugRoutingTests
    {
        private sealed class CapturingRelayer : IPlayerTextRelayer
        {
            public readonly List<(string message, bool isDebug)> Sent = new();
            public void SendLogMessageToAll(string message, TextColor color, bool isDebug = false)
                => Sent.Add((message, isDebug));
        }

        [Test]
        public void PlayerLogSender_Log_SendsWithoutDebugFlag()
        {
            var relayer = new CapturingRelayer();
            var sender = new PlayerLogSender(relayer);

            sender.Log("normal");

            Assert.That(relayer.Sent, Has.Count.EqualTo(1));
            Assert.That(relayer.Sent[0].message, Is.EqualTo("normal"));
            Assert.That(relayer.Sent[0].isDebug, Is.False);
        }

        [Test]
        public void PlayerLogSender_LogDebug_SendsWithDebugFlag()
        {
            var relayer = new CapturingRelayer();
            var sender = new PlayerLogSender(relayer);

            sender.LogDebug("dev detail");

            Assert.That(relayer.Sent, Has.Count.EqualTo(1));
            Assert.That(relayer.Sent[0].message, Is.EqualTo("dev detail"));
            Assert.That(relayer.Sent[0].isDebug, Is.True);
        }

        // A sink that only implements DisplayLogMessage: the DisplayDebugMessage default routes debug lines
        // to the normal display so nothing is silently dropped on a front end that doesn't split them out.
        private sealed class PlainSink : ILogMessageUI
        {
            public readonly List<string> Normal = new();
            public void DisplayLogMessage(string message, TextColor color) => Normal.Add(message);
        }

        [Test]
        public void DisplayDebugMessage_DefaultsToNormalDisplay()
        {
            ILogMessageUI sink = new PlainSink();

            sink.DisplayDebugMessage("dev detail", TextColor.White);

            Assert.That(((PlainSink)sink).Normal, Is.EqualTo(new[] { "dev detail" }));
        }

        // A sink that separates the two categories (like the GUI) receives debug lines on its own path.
        private sealed class SplittingSink : ILogMessageUI
        {
            public readonly List<string> Normal = new();
            public readonly List<string> Debug = new();
            public void DisplayLogMessage(string message, TextColor color) => Normal.Add(message);
            public void DisplayDebugMessage(string message, TextColor color) => Debug.Add(message);
        }

        [Test]
        public void DisplayDebugMessage_Override_ReceivesDebugSeparately()
        {
            ILogMessageUI sink = new SplittingSink();

            sink.DisplayLogMessage("normal", TextColor.White);
            sink.DisplayDebugMessage("dev detail", TextColor.White);

            var splitting = (SplittingSink)sink;
            Assert.That(splitting.Normal, Is.EqualTo(new[] { "normal" }));
            Assert.That(splitting.Debug, Is.EqualTo(new[] { "dev detail" }));
        }

        // A controller that doesn't override the debug-aware overload: the default forwards to the plain
        // SendLogMessage, so AI/test controllers keep working unchanged.
        private sealed class PlainController : IPlayerController
        {
            public readonly List<string> Sent = new();
            public string Name => "plain";
            public PlayerID ID { get; } = new PlayerID(System.Guid.NewGuid());
            public bool IsReady => true;
            public FDG.Presentation.IPresentationSink? PresentationSink => null;
            public event System.Action<bool>? OnReadyStateChanged;
            public event System.Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
            public System.Threading.Tasks.Task WaitUntilReadyAsync() => System.Threading.Tasks.Task.CompletedTask;
            public void SendLogMessage(string logMessage, TextColor color) => Sent.Add(logMessage);
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }

        [Test]
        public void SendLogMessage_DebugOverload_DefaultsToPlainSend()
        {
            IPlayerController controller = new PlainController();

            controller.SendLogMessage("dev detail", TextColor.White, isDebug: true);

            Assert.That(((PlainController)controller).Sent, Is.EqualTo(new[] { "dev detail" }));
        }
    }
}
