
using FDG.Data;

namespace FDG
{
    public class ArmyData : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        public IReadOnlyList<IUnit> Units => UnitBindings.Select(bind => bind.GetValue())
            .Cast<IUnit>()
            .ToList();

        private List<DataReference> _unitReferences;

        public List<DataBinding<UnitData>> UnitBindings;

        public ArmyData(IArmyTemplate armyToCopy, List<DataReference> unitReferences,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            PlayerID = armyToCopy.PlayerID;

            _unitReferences = unitReferences;

            UnitBindings = new List<DataBinding<UnitData>>();
            foreach (DataReference unit in unitReferences)
            {
                DataBinding<UnitData> unitBinding = new DataBinding<UnitData>(commandProcessor,
                    gameDataStore, unit);
                UnitBindings.Add(unitBinding);
            }
        }
    }
}
