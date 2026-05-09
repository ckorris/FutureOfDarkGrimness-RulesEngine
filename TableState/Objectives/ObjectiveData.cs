using FDG.Data;
using Newtonsoft.Json;

namespace FDG
{
    public interface IObjective
    {
        Position Position { get; }
        PlayerID? OwnerID { get; }
        void SetOwner(PlayerID? ownerID);
    }

    public class ObjectiveData : IObjective
    {
        // Guid.Empty stored in the binding means "no owner".
        private static readonly PlayerID Unowned = new PlayerID(Guid.Empty);

        public Position Position { get; }

        public DataBinding<PlayerID> OwnerIDBinding;

        public PlayerID? OwnerID
        {
            get
            {
                var id = OwnerIDBinding.GetValue();
                return id.ID == Guid.Empty ? null : id;
            }
        }

        [JsonConstructor]
        public ObjectiveData(Position position, DataBinding<PlayerID> ownerIDBinding)
        {
            Position = position;
            OwnerIDBinding = ownerIDBinding;
        }

        public ObjectiveData(Position position, IReadWriteableGameDataStore gameDataStore)
        {
            Position = position;
            DataReference ownerRef = gameDataStore.Create(Unowned);
            OwnerIDBinding = gameDataStore.GetDataBinding<PlayerID>(ownerRef);
        }

        public void SetOwner(PlayerID? ownerID)
        {
            OwnerIDBinding.SetValue(ownerID ?? Unowned);
        }
    }
}
