using FDG.Players;
using FutureOfDarkGrimness.TextInterface;

namespace FDG.TextInterface
{
    internal class LogAndChatMessageRelayer : IPlayerTextRelayer
    {
        private PlayerSlotManager _playerSlotManager;

        public LogAndChatMessageRelayer(PlayerSlotManager playerSlotManager)
        {
            _playerSlotManager = playerSlotManager;
        }

        public void SendLogMessageToAll(string message)
        {
            foreach (PlayerSlot slot in _playerSlotManager.PlayerSlots)
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

            foreach (PlayerSlot slot in _playerSlotManager.PlayerSlots)
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

            foreach (PlayerSlot slot in _playerSlotManager.PlayerSlots)
            {
                if (slot.IsFilled == false)
                {
                    continue;
                }

                if (slot != sendingSlot && slot.TeamNumber == sendingSlot.TeamNumber)
                {
                    slot.Controller.SendPlayerMessage(sendingSlot.Name, EChatMessageType.Team, message);

                }
            }
        }

        public void SendDirectPlayerMessage(PlayerID sendingPlayer, PlayerID targetPlayer, string message)
        {
            PlayerSlot sendingSlot = _playerSlotManager.GetSlotByID(sendingPlayer);
            PlayerSlot targetSlot = _playerSlotManager.GetSlotByID(targetPlayer);

            if (targetSlot.IsFilled == false)
            {
                System.Diagnostics.Debug.WriteLine("Can't send message to player because slot is unassigned.");
                return;
            }

            targetSlot.Controller.SendPlayerMessage(sendingSlot.Name, EChatMessageType.Direct, message);
        }
    }
}
