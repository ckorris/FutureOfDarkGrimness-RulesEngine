
using FDG.Data;
using FDG.Data.Serialization;

namespace FDG
{
    public class ArmyData : IArmy, IGameDataAware
    {
        public PlayerID PlayerID { get; private set; }

        public IReadOnlyList<IUnit> Units => UnitBindings.Select(bind => bind.GetValue())
            .Cast<IUnit>()
            .ToList();

        private List<DataReference> _unitReferences;

        public List<DataBinding<UnitData>> UnitBindings;

        public ArmyData(IArmyTemplate armyToCopy, List<DataReference> unitReferences,
            IReadWriteableGameDataStore gameDataStore)
        {
            PlayerID = armyToCopy.PlayerID;

            _unitReferences = unitReferences;

            SetGameDataStore(gameDataStore);
        }

        public void SetGameDataStore(IReadWriteableGameDataStore gameDataStore)
        {
            UnitBindings = new List<DataBinding<UnitData>>();
            foreach (DataReference unit in _unitReferences)
            {
                DataBinding<UnitData> unitBinding = gameDataStore.GetDataBinding<UnitData>(unit);
                UnitBindings.Add(unitBinding);
            }
        }
    }
}
