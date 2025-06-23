using FDG.StageResolution;
using System.Runtime.CompilerServices;

namespace FDG.Players
{
    internal class PlayerSlotManager : IPlayerRequestByID
    {
        /// <summary>
        /// Gets a copy of the player slots array presented as info that's publicly available and UI-friendly.
        /// </summary>
        public IPlayerSlotInfo[] SlotInfos
        {
            get
            {
                IPlayerSlotInfo[] infos = new IPlayerSlotInfo[PlayerSlots.Length];
                Array.Copy(PlayerSlots, infos, infos.Length);
                return infos;
            }
        }

        public bool AreAllSlotsAssigned
        {
            get
            {
                foreach(PlayerSlot playerSlot in PlayerSlots)
                {
                    if(playerSlot.IsFilled == false)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        internal PlayerSlot[] PlayerSlots;

        public PlayerSlotManager(PlayerSlot[] playerSlots)
        {
            PlayerSlots = playerSlots;
        }

        public bool TryGetNextOpenSlotID(out int? nextSlotID)
        {
            for(int i = 0; i < PlayerSlots.Length; i++)
            {
                if (PlayerSlots[i].IsFilled == false)
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
            if(slotID >  PlayerSlots.Length)
            {
                throw new IndexOutOfRangeException($"Tried to assign a {nameof(playerController)} named {playerController.Name} " + 
                    $"to slot ID {slotID}, when there are only {PlayerSlots.Length} slots.");
            }

            PlayerSlots[slotID].AssignPlayerController(playerController);

            return PlayerSlots[slotID].PlayerID;
        }

        public Task WaitUntilAllSlotsReady()
        {
            List<Task> playerReadyTasks = new List<Task>(PlayerSlots.Length);

            foreach (PlayerSlot slot in PlayerSlots)
            {
                if (slot.IsFilled == false)
                {
                    throw new InvalidOperationException("Tried to await all slots to be ready when not all were assigned.");
                }

                playerReadyTasks.Add(slot.Controller.WaitUntilReadyAsync());
            }

            System.Diagnostics.Debug.WriteLine($"Awaiting {playerReadyTasks.Count} player(s) to be ready.");

            return Task.WhenAll(playerReadyTasks);
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

        internal PlayerSlot GetSlotByID(PlayerID playerID)
        {
            PlayerSlot? playerSlot = PlayerSlots.FirstOrDefault(slot => slot.PlayerID == playerID);

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
