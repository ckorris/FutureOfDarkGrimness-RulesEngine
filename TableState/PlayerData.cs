
namespace FDG
{
    public record PlayerData : IPlayer
    {
        public string Name { get; private set; }

        public PlayerID ID { get; private set; }

        public PlayerData(string name, PlayerID id)
        {
            Name = name;
            ID = id;
        }

        public static PlayerData CreateWithRandomID(string name)
        {
            Guid guid = Guid.NewGuid();
            PlayerID playerID = new PlayerID(guid);
            
            return new PlayerData(name, playerID);
        }
    }
}
