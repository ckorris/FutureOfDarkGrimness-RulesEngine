using FDG.Data;
using FDG.Data.Serialization;
using System.Text.Json.Serialization;

namespace FDG
{
    public class UnitData : IUnit, IGameDataAware
    {
        public PlayerID PlayerID { get; private set; }

        public string Name { get; }

        public int Quality { get; }

        public int Defense { get; }

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

        public List<ISpecialRule> SpecialRules { get; } //TODO: Implement, looking at models.

        public List<IModel> Models => ModelBindings.Select(binding => binding.GetValue())
            .Cast<IModel>()
            .ToList();

        private List<DataReference> _modelReferences;

        public List<DataBinding<ModelData>> ModelBindings;

        [JsonConstructor]
        public UnitData(PlayerID playerID, string name, int quality, int defense, 
            List<ISpecialRule> specialRules, List<DataReference> modelReferences)
        {
            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

            SpecialRules = specialRules;

            _modelReferences = modelReferences;
        }

        

        public UnitData(IUnitTemplate unitToCopy, List<DataReference> modelReferences, IReadWriteableGameDataStore gameDataStore)
        {
            PlayerID = unitToCopy.PlayerID;
            Name = unitToCopy.Name;
            Quality = unitToCopy.Quality;
            Defense = unitToCopy.Defense;

            _modelReferences = modelReferences;

            ModelBindings = new List<DataBinding<ModelData>>();
            foreach (DataReference model in modelReferences)
            {
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(model);
                ModelBindings.Add(modelBinding);
                ((IModel)modelBinding.GetValue()).OnWoundsDealt += OnModelWoundsDealt;
            }

            //TEMP
            SpecialRules = new List<ISpecialRule>();
        }

        public void SetGameDataStore(IReadWriteableGameDataStore gameDataStore)
        {
            ModelBindings = new List<DataBinding<ModelData>>();
            foreach (DataReference model in _modelReferences)
            {
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(model);
                ModelBindings.Add(modelBinding);
                ((IModel)modelBinding.GetValue()).OnWoundsDealt += OnModelWoundsDealt;
            }
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
