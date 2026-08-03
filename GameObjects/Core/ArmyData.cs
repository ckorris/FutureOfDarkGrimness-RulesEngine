
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using Newtonsoft.Json;

namespace FDG
{
    public class ArmyData : IArmy
    {
        public PlayerID PlayerID { get; private set; }

        // #329: the army file's display identity (list name, faction, points limit), carried into the
        // game so the in-game army list can head each player's tab the way the printed list does.
        // Display-only. Public settable so all three ride the store to clients and survive saves; a
        // pre-#329 save reads back ""/0 and the display hides them. Set at army load (CreateArmy).
        public string ArmyName { get; set; } = string.Empty;

        public string Faction { get; set; } = string.Empty;

        public int PointsLimit { get; set; }

        public List<DataBinding<UnitData>> UnitBindings;

        // The army-wide spell list (#033), resolved from the embedded army file at creation. [JsonIgnore]
        // for the same reason UnitData.RuleDefinitions is: spells resolve from embedded data at load, not
        // from serialized game state, and only the host — which runs the cast stage — reads them (the
        // client receives the castable spells inside the cast request payload).
        [JsonIgnore] private IReadOnlyList<RuntimeSpell> _spells = System.Array.Empty<RuntimeSpell>();

        [JsonIgnore] public IReadOnlyList<RuntimeSpell> Spells => _spells;

        public void SetSpells(IReadOnlyList<RuntimeSpell> spells) => _spells = spells;

        // #095: the army file's embedded rule definitions (#059) + spell list (#033) as an STJ blob, so a
        // resume can rebuild the two things above that are otherwise unrecoverable — the shared rule
        // resolver's non-core entries (which granted-rule tokens resolve their names against) and _spells
        // itself. Newtonsoft carries it as an opaque string; see ArmyRuleDataPersistence for why the army
        // file itself can't be consulted on resume.
        [JsonProperty] private string? _armyRuleDataJson;

        /// <summary>Records the army file's rule definitions and spells for <see cref="RestoreRuleData"/>
        /// to read back after a save/load resume. Called at army load, where the file is still in hand.</summary>
        public void PersistRuleData(IReadOnlyList<SpecialRuleDefinition> ruleDefinitions,
            IReadOnlyList<SpellDefinition> spells,
            IReadOnlyList<SaveLoad.UnitFileEntry>? auxiliaryUnits = null,
            string? defaultRangedEffectSet = null, string? defaultMeleeEffectSet = null)
        {
            _armyRuleDataJson = ArmyRuleDataPersistence.Serialize(ruleDefinitions, spells,
                auxiliaryUnits, defaultRangedEffectSet, defaultMeleeEffectSet);
        }

        /// <summary>The persisted army-file data, or null when this army carries none (built outside army
        /// load, or loaded from a pre-#095 save).</summary>
        public ArmyRuleDataPersistence.PersistedArmyRuleData? RestoreRuleData()
            => ArmyRuleDataPersistence.Deserialize(_armyRuleDataJson);

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
