using FDG.Players;
using System.Drawing;
using System.Numerics;
using static System.Formats.Asn1.AsnWriter;

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

            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                if(TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.AddVisual(visual);
            }
        }

        public void RemoveVisual(Guid visualID)
        {
            if(_currentVisuals.Remove(visualID) == false)
            {
                throw new ArgumentException($"Tried to remove a visual with ID {visualID} but none was found.");
            }

            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                if (TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.RemoveVisual(visualID);
            }
        }

        public void UpdateVisualTransform(Guid visualID, Position position, Quaternion rotation, Vector3 scale)
        {
            if (_currentVisuals.ContainsKey(visualID))
            {
                throw new ArgumentException($"Tried to update a visual with ID {visualID} but it wasn't found.");
            }

            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                if (TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.UpdateVisualTransform(visualID, position, rotation, scale);
            }
        }

        public void UpdateVisualColor(Guid visualID, Color color)
        {
            if (_currentVisuals.ContainsKey(visualID))
            {
                throw new ArgumentException($"Tried to update a visual with ID {visualID} but it wasn't found.");
            }

            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                if (TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.UpdateVisualColor(visualID, color);
            }
        }

        public void ClearAllVisuals()
        {
            for (int i = 0; i < _playerSlotManager._playerSlots.Length; i++)
            {
                if (TryGetPlayerVisualDrawer(i, out ITempVisualDrawer? visualDrawer) == false)
                {
                    continue;
                }

                visualDrawer?.ClearAllVisuals();
            }

            _currentVisuals.Clear();
        }



        private bool TryGetPlayerVisualDrawer(int playerSlot, out ITempVisualDrawer? visualDrawer)
        {
            IPlayerController? controller = _playerSlotManager._playerSlots[playerSlot].Controller;
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
