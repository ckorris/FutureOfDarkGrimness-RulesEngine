using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    internal class PlayerManager //TODO: I don't like "Manager" in names. Brainstorm.
    {
        private PlayerSlot[] PlayerSlots;


        public PlayerManager(int slotCount)
        {
            PlayerSlots = new PlayerSlot[slotCount];
            for (int i = 0; i < PlayerSlots.Length; i++)
            {
                PlayerSlots[i] = new PlayerSlot(slotID: i, teamNumber: i);
            }

        }

        public PlayerManager(PlayerSlot[] playerSlots)
        {
            PlayerSlots = playerSlots;
        }

        public class PlayerSlot
        {
            public int SlotID;

            public int TeamNumber; //TODO: Kinda wanna use TeamID.

            public PlayerID PlayerID;

            public IPlayerController? Controller;

            public PlayerSlot(int slotID, int teamNumber)
            {
                SlotID = slotID;
                TeamNumber = teamNumber;
                PlayerID = new PlayerID(Guid.NewGuid());
            }

            public PlayerSlot(int slotID, int teamNumber, PlayerID playerID)
            {
                SlotID = slotID;
                TeamNumber = teamNumber;
                PlayerID = playerID;
            }



        }

    }
}
