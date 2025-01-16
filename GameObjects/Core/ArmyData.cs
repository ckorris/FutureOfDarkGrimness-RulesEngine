
using FDG.Data;

namespace FDG
{
    public class ArmyData : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        public IReadOnlyList<IUnit> Units => _unitBindings.Select(bind => bind.GetValue())
            .Cast<IUnit>()
            .ToList();

        private List<DataReference> _unitReferences;

        private List<DataBinding<UnitData>> _unitBindings;

        public ArmyData(IArmyTemplate armyToCopy, List<DataReference> unitReferences,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            PlayerID = armyToCopy.PlayerID;

            _unitReferences = unitReferences;

            _unitBindings = new List<DataBinding<UnitData>>();
            foreach (DataReference unit in unitReferences)
            {
                DataBinding<UnitData> unitBinding = new DataBinding<UnitData>(commandProcessor,
                    gameDataStore, unit);
                _unitBindings.Add(unitBinding);
            }
        }
    }
}
