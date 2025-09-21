using FDG.MessageBus;
using FDG.Players;
using FDG.TextInterface;

namespace FDG.Network.Messages
{
    internal class LogChatMessageEndpoint : IDisposable
    {
        private ILogMessageUI _logMessageUI;
        private IPlayerMessageUI _playerMessageUI;
        private ITableState _tableState;
        private List<PlayerID> _localPlayerIDs;
        private IMessageBusClient _messageBusClient;

        public LogChatMessageEndpoint(ILogMessageUI logMessageUI, IPlayerMessageUI playerMessageUI,
            ITableState tableState, List<PlayerID> localPlayerIDs, IMessageBusClient messageBusClient)
        {
            if(localPlayerIDs.Count == 0)
            {
                throw new ArgumentException($"{nameof(LogChatMessageEndpoint)} constructor passed an empty list of players.");
            }

            _logMessageUI = logMessageUI;
            _playerMessageUI = playerMessageUI;
            _tableState = tableState;
            _localPlayerIDs = localPlayerIDs;
            _messageBusClient = messageBusClient;

            _messageBusClient.RegisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _messageBusClient.RegisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);

            _playerMessageUI.OnMessageSentByPlayer += SendChatMessage;
        }

        public void Dispose()
        {
            _messageBusClient.DeregisterForMessageEvent<LogChatNetworkMessage>(OnLogMessageReceived);
            _messageBusClient.DeregisterForMessageEvent<PlayerChatNetworkMessage>(OnPlayerMessageReceived);
            _playerMessageUI.OnMessageSentByPlayer -= SendChatMessage;
        }

        private void SendChatMessage(string message, EChatMessageType messageType)
        {
            //Just use the first player. If multiple people are playing on one device and you use the chat to send a message,
            //then assume it's the first person sending it.
            //TODO: This messes with sending to the same team, but that's kind of a messed up concept anyway if you have multiple
            //people on one device on different teams but also are connected to a game with other players.
            PlayerID sendingPlayerID = _localPlayerIDs[0];
            NetworkPlayerSubmitChatMessage chatMessage = new NetworkPlayerSubmitChatMessage(sendingPlayerID, messageType, message);
            _messageBusClient.SendCommandToHostAsync(chatMessage);
        }

        private void OnLogMessageReceived(LogChatNetworkMessage message)
        {
            _logMessageUI?.DisplayLogMessage(message.LogMessage);
        }

        private void OnPlayerMessageReceived(PlayerChatNetworkMessage message)
        {
            if (_playerMessageUI == null)
            {
                return;
            }

            IPlayerSlotInfo sendingPlayerSlot = _tableState.Players.Objects
                .First(player => player.PlayerID == message.PlayerID);

            switch (message.MessageType)
            {
                case EChatMessageType.Global:
                    _playerMessageUI?.DisplayPlayerMessage(sendingPlayerSlot.Name, message.MessageType, message.Message);
                    break;
                case EChatMessageType.Team:
                    ITeam sendingTeam = _tableState.Teams.Objects.First(team => team.TeamNumber == sendingPlayerSlot.TeamNumber);

                    //Show the message if _any_ local players are on that team.
                    foreach(PlayerID localID in _localPlayerIDs)
                    {
                        if(sendingTeam.IsPlayerOnTeam(localID))
                        {
                            _playerMessageUI?.DisplayPlayerMessage(sendingPlayerSlot.Name, message.MessageType, message.Message);
                            break; //Don't show more than once.
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

        }
    }
}
