using FDG.Data;
using FDG.Network.Connection;
using FDG.StageResolution;
using System.Runtime.CompilerServices;

namespace FDG.Players
{
    internal class PlayerSlotManager
    {
        public int PlayerCount => _playerSlots.Length;

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

        // #179: private - outside readers go through PlayerSlots, never the field.
        private readonly PlayerSlot[] _playerSlots;
        internal PlayerSlot[] PlayerSlots => _playerSlots;

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
            if(slotID < 0 || slotID >= _playerSlots.Length)
            {
                throw new IndexOutOfRangeException($"Tried to assign a {nameof(playerController)} named {playerController.Name} " + 
                    $"to slot ID {slotID}, when there are only {_playerSlots.Length} slots.");
            }

            _playerSlots[slotID].AssignPlayerController(playerController);

            return _playerSlots[slotID].PlayerID;
        }

        public Task WaitUntilAllSlotsReady()
        {
            List<Task> playerReadyTasks = new List<Task>(_playerSlots.Length);

            foreach (PlayerSlot slot in _playerSlots)
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

        /*
        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) 
            where TRequest : IStageTaskRequest<TReply>
        {
            IPlayerController playerController = GetPlayerControllerByID(request.TargetPlayerID);
            return playerController.RequestDecision<TRequest, TReply>(request);
            //TODO: I think we want to send globally via the message bus, but look into
            //how the requests are serialized. I wanna avoid serializing and deserializing
            //locally if possible, but also avoid hacks.
        }
        */

        private IPlayerController GetPlayerControllerByID(PlayerID playerID)
        {
            PlayerSlot playerSlot = GetSlotByID(playerID);

            if(playerSlot.Controller == null)
            {
                throw new NoPlayerControllerAssignedException(playerSlot);
            }

            return playerSlot.Controller;
        }

        /// <summary>
        /// Maps a network <see cref="ConnectionID"/> back to the <see cref="PlayerID"/> playing on it, if any
        /// slot is filled by a <see cref="NetworkPlayerController"/> on that connection. Used by the
        /// disconnect lifecycle (#076) to fail a dropped player's pending requests.
        /// </summary>
        public bool TryGetPlayerIDByConnection(ConnectionID connectionID, out PlayerID playerID)
        {
            foreach (PlayerSlot slot in _playerSlots)
            {
                if (slot.Controller is NetworkPlayerController networkController
                    && networkController.ConnectionID == connectionID)
                {
                    playerID = slot.PlayerID;
                    return true;
                }
            }

            playerID = default;
            return false;
        }

        /// <summary>
        /// Maps a <see cref="PlayerID"/> to the network <see cref="ConnectionID"/> it plays on, if the
        /// slot is filled by a <see cref="NetworkPlayerController"/>. Returns false for a local (in-process)
        /// or unassigned player — they have no connection. The mirror of
        /// <see cref="TryGetPlayerIDByConnection"/>; used to route a decision request to just the target
        /// player instead of broadcasting it to every client (#088).
        /// </summary>
        public bool TryGetConnectionByPlayerID(PlayerID playerID, out ConnectionID connectionID)
        {
            PlayerSlot? slot = _playerSlots.FirstOrDefault(s => s.PlayerID == playerID);

            if (slot?.Controller is NetworkPlayerController networkController)
            {
                connectionID = networkController.ConnectionID;
                return true;
            }

            connectionID = default;
            return false;
        }

        internal PlayerSlot GetSlotByID(PlayerID playerID)
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
        Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>;
    }
}
