using FDG.Players;
using FutureOfDarkGrimness.TextInterface;

namespace FDG.TextInterface
{
    internal class LogAndChatMessageRelayer : IPlayerTextRelayer
    {
        private PlayerSlotManager _playerSlotManager;

        public LogAndChatMessageRelayer(PlayerSlotManager playerSlotManager)
        {
            if(playerSlotManager == null)
            {
                throw new NullReferenceException($"{nameof(PlayerSlotManager)} passed into {nameof(LogAndChatMessageRelayer)} " + 
                    "constructor was null.");
            }

            _playerSlotManager = playerSlotManager;

            for(int i = 0; i < playerSlotManager._playerSlots.Length; i++)
            {
                playerSlotManager._playerSlots[i].Controller.OnMessageSentByPlayer += OnPlayerSentMessage;

            }
        }

        private void OnPlayerSentMessage(PlayerID playerID, EChatMessageType chatMessageType, string message)
        {
            switch(chatMessageType)
            {
                case EChatMessageType.Global:
                    SendGlobalPlayerMessage(playerID, message);
                    break;
                case EChatMessageType.Team:
                    SendTeamPlayerMessage(playerID, message);
                    break;
                default:
                    throw new System.IndexOutOfRangeException();
            }
        }

        public void SendLogMessageToAll(string message)
        {
            foreach (PlayerSlot slot in _playerSlotManager._playerSlots)
            {
                if (slot.IsFilled == false)
                {
                    continue;
                }

                slot.Controller.SendLogMessage(message);
            }
        }

        public void SendGlobalPlayerMessage(PlayerID sendingPlayer, string message)
        {
            string sourcePlayerName = _playerSlotManager.GetSlotByID(sendingPlayer).Name;

            foreach (PlayerSlot slot in _playerSlotManager._playerSlots)
            {
                if (slot.IsFilled == false)
                {
                    continue;
                }

                slot.Controller.SendPlayerMessage(sourcePlayerName, EChatMessageType.Global, message);
            }
        }

        public void SendTeamPlayerMessage(PlayerID sendingPlayer, string message)
        {
            PlayerSlot sendingSlot = _playerSlotManager.GetSlotByID(sendingPlayer);

            foreach (PlayerSlot slot in _playerSlotManager._playerSlots)
            {
                if (slot.IsFilled == false)
                {
                    continue;
                }

                if (slot.TeamNumber == sendingSlot.TeamNumber)
                {
                    slot.Controller.SendPlayerMessage(sendingSlot.Name, EChatMessageType.Team, message);

                }
            }
        }

    }
}
