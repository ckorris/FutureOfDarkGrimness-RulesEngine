using FDG.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG
{
    public class UnitData : IUnit
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

        public event DataValueChangedHandler<float> OnWoundsDealt;

        public List<ISpecialRule> SpecialRules { get; } //TODO: Implement, looking at models.

        public List<IModel> Models => _modelBindings.Select(binding => binding.GetValue())
            .Cast<IModel>()
            .ToList();

        private List<DataReference> _modelReferences;

        private List<DataBinding<ModelData>> _modelBindings;

        /*
        public Unit(PlayerID playerID, string name, int quality, int defense, List<IModel> models,
            List<ISpecialRule> specialRules)
        {
            TODO: Redo this constructor to refer to models as data in the data store.

            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;
            Models = models;
            SpecialRules = specialRules;

            foreach (IModel model in models)
            {
                model.OnWoundsDealt += OnModelWoundsDealt;
            }
        }
        */

        public UnitData(IUnit unitToCopy, List<DataReference> modelReferences,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            PlayerID = unitToCopy.PlayerID;
            Name = unitToCopy.Name;
            Quality = unitToCopy.Quality;
            Defense = unitToCopy.Defense;

            _modelReferences = modelReferences;

            _modelBindings = new List<DataBinding<ModelData>>();
            foreach (DataReference model in modelReferences)
            {
                DataBinding<ModelData> modelBinding = new DataBinding<ModelData>(commandProcessor,
                    gameDataStore, model);
                _modelBindings.Add(modelBinding);
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
