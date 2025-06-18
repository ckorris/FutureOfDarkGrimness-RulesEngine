using FDG.SaveLoad;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.Network.Messages
{
    public record ArmyListUpdateMessage(PlayerID playerID, ArmyListFile armyListFile);
}
