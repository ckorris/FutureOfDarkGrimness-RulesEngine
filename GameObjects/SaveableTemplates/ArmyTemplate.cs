
namespace FDG
{
    public interface IArmyTemplate
    {
        PlayerID PlayerID { get; }

        List<IUnitTemplate> Units { get; }
    }

    public class ArmyTemplate : IArmyTemplate
    {
        public PlayerID PlayerID { get; }

        public List<IUnitTemplate> Units { get; }

        public ArmyTemplate(PlayerID playerID, List<IUnitTemplate>? units)
        {
            PlayerID = playerID;
            Units = units == null ? new List<IUnitTemplate>() : units;
        }
    }
}
