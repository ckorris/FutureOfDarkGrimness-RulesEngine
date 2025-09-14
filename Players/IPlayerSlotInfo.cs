
using FDG.Data;
using Newtonsoft.Json;

namespace FDG.Players
{
    /// <summary>
    /// Public-facing knowledge about player slots that can be used for the UI and stuff.
    /// </summary>
    public interface IPlayerSlotInfo
    {
        public PlayerID PlayerID { get; }

        int SlotID { get; }

        int TeamNumber { get; }

        string Name { get; }

        bool IsFilled { get; }
    }

    public struct PlayerSlotInfo : IPlayerSlotInfo
    {
        public PlayerID PlayerID { get; }

        public int SlotID { get; }

        public int TeamNumber { get; }

        public string Name { get; set; }

        public bool IsFilled { get; set; }


        [JsonConstructor]
        public PlayerSlotInfo(PlayerID playerID, int slotID, int teamNumber, string name, bool isFilled)
        {
            PlayerID = playerID;
            SlotID = slotID;
            TeamNumber = teamNumber;
            Name = name;
            IsFilled = isFilled;
        }

        public PlayerSlotInfo(PlayerSlot slot)
        {
            PlayerID = slot.PlayerID;
            SlotID = slot.SlotID;
            TeamNumber = slot.TeamNumber;
            Name = slot.Name;
            IsFilled = slot.IsFilled;
        }
    }
}
