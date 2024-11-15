using System;

namespace FDG
{
    public record PlayerInfo
    {
        public string Name;

        public PlayerID ID;

        public PlayerInfo(string name, PlayerID id)
        {
            Name = name;
            ID = id;
        }

        public static PlayerInfo CreateWithRandomID(string name)
        {
            Guid guid = Guid.NewGuid();
            PlayerID playerID = new PlayerID(guid);
            
            return new PlayerInfo(name, playerID);
        }
    }
}
