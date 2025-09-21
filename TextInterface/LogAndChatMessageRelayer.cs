using FDG.MessageBus;
using FDG.Network.Messages;
using FutureOfDarkGrimness.TextInterface;

namespace FDG.TextInterface
{
    internal class LogAndChatMessageRelayer : IPlayerTextRelayer
    {
        private IMessageBusHost _messageBusHost;

        public LogAndChatMessageRelayer(IMessageBusHost messageBusHost)
        {
            if(messageBusHost == null)
            {
                throw new NullReferenceException($"{nameof(IMessageBusHost)} passed into {nameof(LogAndChatMessageRelayer)} " + 
                    "constructor was null.");
            }

            _messageBusHost = messageBusHost;
            _messageBusHost.RegisterForMessageEvent<NetworkPlayerSubmitChatMessage>(OnPlayerSentMessage);
        }

        private void OnPlayerSentMessage(NetworkPlayerSubmitChatMessage message)
        {
            PlayerChatNetworkMessage globalMessage = new PlayerChatNetworkMessage(message.PlayerID, message.MessageType, message.Message);
            _messageBusHost.SendCommandToAllAsync(globalMessage);
        }

        public void SendLogMessageToAll(string message)
        {
            LogChatNetworkMessage logMessage = new LogChatNetworkMessage(message);
            _messageBusHost.SendCommandToAllAsync(logMessage);
        }

    }
}
