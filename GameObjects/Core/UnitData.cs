using FDG.Data;
using Newtonsoft.Json;
namespace FDG
{
    public class UnitData : IUnit
    {
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
            List<SpecialRule> specialRules, List<DataBinding<ModelData>> modelBindings)
        {
            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

            SpecialRules = specialRules;

            ModelBindings = modelBindings;
        }

        public UnitData(IUnitTemplate unitToCopy, List<DataReference> modelReferences, IReadWriteableGameDataStore gameDataStore)
        {
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
