using Newtonsoft.Json;

namespace FDG
{
    public interface IObjective
    {
        Position Position { get; }
        PlayerID? OwnerID { get; }
    }

    public class ObjectiveData : IObjective
    {
        public Position Position { get; }
        public PlayerID? OwnerID { get; private set; }

        [JsonConstructor]
        public ObjectiveData(Position position)
        {
            Position = position;
        }

        public void SetOwner(PlayerID? ownerID)
        {
            OwnerID = ownerID;
        }
    }
}
