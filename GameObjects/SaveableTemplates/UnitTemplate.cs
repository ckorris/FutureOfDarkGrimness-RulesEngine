
namespace FDG
{
    public interface IUnitTemplate
    {
        PlayerID PlayerID { get; }

        string Name { get; }

        int Quality { get; }

        int Defense { get; }

        List<ModelTemplate> Models { get; }
    }

    public class UnitTemplate : IUnitTemplate
    {
        public PlayerID PlayerID { get; }

        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

        public List<ModelTemplate> Models { get; }

        public UnitTemplate(PlayerID playerID, string name, int quality, int defense,
                List<ModelTemplate> models = null)
        {
            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

            Models = models == null ? new List<ModelTemplate>() : models;
        }
    }
}
