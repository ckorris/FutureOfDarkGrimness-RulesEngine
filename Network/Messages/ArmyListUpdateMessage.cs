using FDG.SaveLoad;

namespace FDG.Network.Messages
{
    public record ArmyListUpdateMessage(PlayerID playerID, ArmyListFile armyListFile);
}
