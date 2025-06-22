using FDG.StageResolution;
using System.Runtime.CompilerServices;

namespace FDG.Players
{
    internal class PlayerSlotManager : IPlayerRequestByID //TODO: I don't like "Manager" in names. Brainstorm.
    {
        /// <summary>
        /// Gets a copy of the player slots array presented as info that's publicly available and UI-friendly.
        /// </summary>
        public IPlayerSlotInfo[] SlotInfos
        {
            get
            {
                IPlayerSlotInfo[] infos = new IPlayerSlotInfo[_playerSlots.Length];
                Array.Copy(_playerSlots, infos, infos.Length);
                return infos;
            }
        }

        public bool AreAllSlotsAssigned
        {
            get
            {
                foreach(PlayerSlot playerSlot in _playerSlots)
                {
                    if(playerSlot.IsFilled == false)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private PlayerSlot[] _playerSlots;

        public PlayerSlotManager(int slotCount)
        {
            _playerSlots = new PlayerSlot[slotCount];
            for (int i = 0; i < _playerSlots.Length; i++)
            {
                _playerSlots[i] = new PlayerSlot(slotID: i, teamNumber: i);
            }
        }

        public PlayerSlotManager(PlayerSlot[] playerSlots)
        {
            _playerSlots = playerSlots;
        }

        public bool TryGetNextOpenSlotID(out int? nextSlotID)
        {
            for(int i = 0; i < _playerSlots.Length; i++)
            {
                if (_playerSlots[i].IsFilled == false)
                {
                    nextSlotID = i;
                    return true;
                }
            }

            nextSlotID = null;
            return false;
        }

        public PlayerID AssignControllerToSlot(int slotID, IPlayerController playerController)
        {
            if(slotID >  _playerSlots.Length)
            {
                throw new IndexOutOfRangeException($"Tried to assign a {nameof(playerController)} named {playerController.Name} " + 
                    $"to slot ID {slotID}, when there are only {_playerSlots.Length} slots.");
            }

            _playerSlots[slotID].AssignPlayerController(playerController);

            return _playerSlots[slotID].PlayerID;
        }

        public Task<TReply> RequestDecision<TRequest, TReply>(PlayerID playerId, TRequest request) 
            where TRequest : IStageTaskRequest<TReply>
        {
            IPlayerController playerController = GetPlayerControllerByID(playerId);
            return playerController.RequestDecision<TRequest, TReply>(request);
        }

        private IPlayerController GetPlayerControllerByID(PlayerID playerID)
        {
            PlayerSlot playerSlot = GetSlotByID(playerID);

            if(playerSlot.Controller == null)
            {
                throw new NoPlayerControllerAssignedException(playerSlot);
            }

            return playerSlot.Controller;
        }

        private PlayerSlot GetSlotByID(PlayerID playerID)
        {
            PlayerSlot? playerSlot = _playerSlots.FirstOrDefault(slot => slot.PlayerID == playerID);

            if(playerSlot == null)
            {
                throw new NoPlayerSlotWithPlayerIDException(playerID);
            }

            return playerSlot;
        }

        private class NoPlayerSlotWithPlayerIDException : Exception
        {
            public NoPlayerSlotWithPlayerIDException(PlayerID playerID, [CallerMemberName] string memberName = "")
                : base($"Method {memberName} tried to perform an operation on a PlayerSlot with player ID {playerID}, " +
                      "but no player with that ID was found.")
            { }
        }

        private class NoPlayerControllerAssignedException : Exception
        {
            public NoPlayerControllerAssignedException(PlayerSlot playerSlot, [CallerMemberName] string memberName = "")
                : base($"Method {memberName} tried to perform an operation on {nameof(IPlayerController)} in slot " + 
                      $"{playerSlot.SlotID}, Player ID {playerSlot.PlayerID}, but no controller was assigned.") { }
        }
    }

    public interface IPlayerRequestByID
    {
        Task<TReply> RequestDecision<TRequest, TReply>(PlayerID playerId, TRequest request)
            where TRequest : IStageTaskRequest<TReply>;
    }
}
