
using FDG.Data;
using Newtonsoft.Json;

namespace FDG
{
    public class ArmyData : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        public List<DataBinding<UnitData>> UnitBindings;

        [JsonIgnore]
        public IReadOnlyList<IUnit> Units => UnitBindings.Select(bind => bind.GetValue())
            .Cast<IUnit>()
            .ToList();

        [JsonConstructor]
        public ArmyData(PlayerID playerId, List<DataBinding<UnitData>> unitBindings)
        {
            PlayerID = playerId;
            UnitBindings = unitBindings;
        }

        public ArmyData(IArmyTemplate armyToCopy, List<DataReference> unitReferences,
            IReadWriteableGameDataStore gameDataStore)
        {
            PlayerID = armyToCopy.PlayerID;

            UnitBindings = new List<DataBinding<UnitData>>();
            foreach (DataReference unit in unitReferences)
            {
                DataBinding<UnitData> unitBinding = gameDataStore.GetDataBinding<UnitData>(unit);
                UnitBindings.Add(unitBinding);
            }
        }
    }
}
