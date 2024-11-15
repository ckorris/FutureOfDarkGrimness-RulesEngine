
namespace FDG
{
    public interface IArmy : IPlayerOwnable
    {
        public HashSet<IUnit> Units { get; }
    }

    public class Army : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        public HashSet<IUnit> Units { get; private set; }

        public Army(PlayerID playerID, HashSet<IUnit> units)
        {
            PlayerID = playerID;
            Units = new HashSet<IUnit>(units); //Copy to prevent modification after creation.
        }
    }
}
