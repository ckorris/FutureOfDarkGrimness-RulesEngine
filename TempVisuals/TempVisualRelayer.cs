using FDG.Players;
using System.Numerics;

namespace FDG.TempVisuals
{
    /// <summary>
    /// Handles the server-side creation of visuals. 
    /// TODO: Not sure if this should inherit from ITempVisualDrawer or its own thing, because 
    /// I expect the methods to be the same but ehhhhhh that muddies the intent.
    /// </summary>
    internal class TempVisualRelayer : ITempVisualDrawer
    {
        private readonly PlayerSlotManager _playerSlotManager;

        private Dictionary<Guid, ITempVisual> _currentVisuals = new Dictionary<Guid, ITempVisual>();


        public TempVisualRelayer(PlayerSlotManager playerSlotManager)
        {
            _playerSlotManager = playerSlotManager;
        }

        public void AddVisual(ITempVisual visual)
        {
            if (_currentVisuals.ContainsKey(visual.ID))
            {
                throw new ArgumentException($"Tried to add a visual with ID {visual.ID} but one was already added.");
            }

            for (int i = 0; i < _playerSlotManager.PlayerSlots.Length; i++)
            {
                if(TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.AddVisual(visual);
            }
        }

        public void RemoveVisual(Guid tempVisualID)
        {
            throw new NotImplementedException();
        }

        public void UpdateVisual(ITempVisual visual)
        {
            throw new NotImplementedException();
        }

        public void UpdateVisualTransform(Guid tempVisualID, Position position, Quaternion rotation, Vector3 scale)
        {
            throw new NotImplementedException();
        }

        public void ClearAllVisuals()
        {
            throw new NotImplementedException();
        }



        private bool TryGetPlayerVisualDrawer(int playerSlot, out ITempVisualDrawer? visualDrawer)
        {
            IPlayerController? controller = _playerSlotManager.PlayerSlots[playerSlot].Controller;
            if (controller == null)
            {
                visualDrawer = default;
                return false;
            }

            visualDrawer = controller.TempVisualDrawer;
            return visualDrawer != null;
        }
    }
}
