
using FDG.Data;
using FDG.Rules.Dispatch;
using Newtonsoft.Json;

namespace FDG
{
    public class ArmyData : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        public List<DataBinding<UnitData>> UnitBindings;

        // The army-wide spell list (#033), resolved from the embedded army file at creation. [JsonIgnore]
        // for the same reason UnitData.RuleDefinitions is: spells resolve from embedded data at load, not
        // from serialized game state, and only the host — which runs the cast stage — reads them (the
        // client receives the castable spells inside the cast request payload).
        [JsonIgnore] private IReadOnlyList<RuntimeSpell> _spells = System.Array.Empty<RuntimeSpell>();

        [JsonIgnore] public IReadOnlyList<RuntimeSpell> Spells => _spells;

        public void SetSpells(IReadOnlyList<RuntimeSpell> spells) => _spells = spells;

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
