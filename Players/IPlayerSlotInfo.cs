using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Players
{
    /// <summary>
    /// Public-facing knowledge about player slots that can be used for the UI and stuff.
    /// </summary>
    public interface IPlayerSlotInfo
    {
        int SlotID { get; }

        int TeamNumber { get; }

        string Name { get; }

        bool IsFilled { get; }
    }
}
