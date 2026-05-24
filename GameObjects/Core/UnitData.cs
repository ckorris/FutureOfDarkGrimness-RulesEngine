using FDG.Data;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using Newtonsoft.Json;
namespace FDG
{
    public class UnitData : IUnit
    {
        public UnitID ID { get; private set; }

        [JsonProperty] private TokenContainer _tokens = new TokenContainer();

        [JsonIgnore] public ITokenContainer Tokens => _tokens;

        public PlayerID PlayerID { get; private set; }

        public string Name { get; set; }

        public int Quality { get; set; }

        public int Defense { get; set; }

        public List<SpecialRule> SpecialRules { get; set; } //TODO: Implement, looking at models.

        public List<DataBinding<ModelData>> ModelBindings;

        [JsonIgnore]
        public float MaxWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds;
                }
                return total;
            }
        }

        [JsonIgnore]
        public float RemainingWounds
        {
            get
            {
                float total = 0;
                foreach (IModel model in Models)
                {
                    total += model.TotalWounds - model.WoundsDealt;
                }
                return total;
            }
        }

        public event DataValueChangedHandler<float>? OnWoundsDealt;

        [JsonIgnore]
        public List<IModel> Models => ModelBindings.Select(binding => binding.GetValue())
            .Cast<IModel>()
            .ToList();

        [JsonIgnore]
        List<ISpecialRule> IUnit.SpecialRules => SpecialRules.Cast<ISpecialRule>().ToList();

        [JsonConstructor]
        public UnitData(PlayerID playerID, string name, int quality, int defense,
            List<SpecialRule> specialRules, List<DataBinding<ModelData>> modelBindings,
            UnitID? id = null)
        {
            ID = id ?? new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

            SpecialRules = specialRules;

            ModelBindings = modelBindings;
        }

        public UnitData(IUnitTemplate unitToCopy, List<DataReference> modelReferences, IReadWriteableGameDataStore gameDataStore)
        {
            ID = new UnitID(System.Guid.NewGuid());

            PlayerID = unitToCopy.PlayerID;
            Name = unitToCopy.Name;
            Quality = unitToCopy.Quality;
            Defense = unitToCopy.Defense;

            ModelBindings = new List<DataBinding<ModelData>>();
            foreach (DataReference model in modelReferences)
            {
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(model);
                ModelBindings.Add(modelBinding);
                ((IModel)modelBinding.GetValue()).OnWoundsDealt += OnModelWoundsDealt;
            }

            //TEMP
            SpecialRules = new List<SpecialRule>();
        }

        public UnitData(PlayerID playerID, UnitFileEntry unitFileEntry, IReadWriteableGameDataStore gameDataStore)
        {
            ID = new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = unitFileEntry.Name;
            Quality = unitFileEntry.Quality;
            Defense = unitFileEntry.Defense;
            SpecialRules = GetRealSpecialRulesFromArmyList(unitFileEntry.SpecialRules);

            ModelBindings = new List<DataBinding<ModelData>>(unitFileEntry.ModelCount);

            List<Weapon> weapons = new List<Weapon>();

            List<WeaponFileEntry> sortedWeaponEntries = (unitFileEntry.Weapons);
            sortedWeaponEntries.Sort((x, y) => x.Quantity.CompareTo(y.Quantity));

            //Distribute weapons in order of quantity, should be a decent approximate of how they're
            //actually distributed.
            List<Weapon> unitWeapons = new List<Weapon>();
            for (int i = 0; i < sortedWeaponEntries.Count; i++)
            {
                WeaponFileEntry weaponEntry = sortedWeaponEntries[i];
                HashSet<ISpecialRule_Weapon> weaponRules = GetRealWeaponSpecialRulesFromEntries(weaponEntry.SpecialRules);

                for (int q = 0; q < weaponEntry.Quantity; q++)
                {
                    Weapon weapon = new Weapon(weaponEntry.Name, weaponEntry.RangeInches, weaponEntry.Attacks,
                            weaponEntry.ArmorPenetration, weaponRules);

                    unitWeapons.Add(weapon);
                }
            }

            for (int i = 0; i < unitFileEntry.ModelCount; i++)
            {
                //TEMP get default base size.
                float tempBaseDiameterInches = 1.1023622f; //28mm.

                List<Weapon> modelWeapons = new List<Weapon>();

                for (int w = i; w < unitWeapons.Count; w += unitFileEntry.ModelCount)
                {
                    modelWeapons.Add(unitWeapons[w]);
                }

                ModelData modelData = new ModelData(tempBaseDiameterInches / 2f, modelWeapons, SpecialRules, new Position(), gameDataStore);

                DataReference modelReference = gameDataStore.Create(modelData);
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(modelReference);
                ModelBindings.Add(modelBinding);
            }
        }

        private List<SpecialRule> GetRealSpecialRulesFromArmyList(List<SpecialRuleEntry> specialRuleEntries)
        {
            //TODO: Implement for real.
            return new List<SpecialRule>();
        }

        private HashSet<ISpecialRule_Weapon> GetRealWeaponSpecialRulesFromEntries(List<SpecialRuleEntry> weaponRules)
        {
            //TODO: Implement for real.
            return new HashSet<ISpecialRule_Weapon>();
        }


        private void OnModelWoundsDealt(float oldWoundsCount, float newWoundsCount)
        {
            //This is called with an individual models' old and new wound count.
            //So to invoke the same for the unit, we get the new wound count, and do math
            //to find out the old.
            float woundsDealt = oldWoundsCount - newWoundsCount;

            float newUnitTotalWounds = RemainingWounds;
            float oldUnitTotalWounds = newUnitTotalWounds + woundsDealt;

            OnWoundsDealt?.Invoke(oldUnitTotalWounds, newUnitTotalWounds);
        }

        public bool GetMobility(out float moveShootDistanceInches, out float chargeDistanceInches)
        {
            //TODO: Process special rules for this.
            moveShootDistanceInches = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES;
            chargeDistanceInches = GameWideConstants.CHARGE_DISTANCE_INCHES;

            return true;
        }


    }
}
