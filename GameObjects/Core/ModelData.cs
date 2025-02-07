using FDG.Data;
using FDG.Data.Serialization;
using System.Text.Json.Serialization;

namespace FDG
{
    public class ModelData : IModel, IGameDataAware
    {
        public float BaseRadiusInches;
        public float TotalWounds { get; }

        private DataReference _remainingWoundsRef;

        private DataBinding<float> _remainingWoundsBinding;

        private DataReference _positionRef;

        public DataBinding<Position> PositionBinding;

        private List<Weapon> _weapons;

        private List<SpecialRule> _specialRules;


        #region IModel Non-Serialized

        float IModel.WoundsDealt => TotalWounds - _remainingWoundsBinding.GetValue();

        Position IModel.Position => PositionBinding.GetValue();

        float IModel.BaseRadiusInches => BaseRadiusInches;

        IReadOnlyList<Weapon> IModel.Weapons => _weapons;

        IReadOnlyList<SpecialRule> IModel.SpecialRules => _specialRules;

        event DataValueChangedHandler<Position> IModel.OnPositionChanged
        {
            add { PositionBinding.OnValueChanged += value; }
            remove { PositionBinding.OnValueChanged -= value; }
        }

        event DataValueChangedHandler<float> IModel.OnWoundsDealt
        {
            add { _remainingWoundsBinding.OnValueChanged += value; }
            remove { _remainingWoundsBinding.OnValueChanged -= value; }
        }

        public void DealWounds(float wounds)
        {
            _remainingWoundsBinding.SetValue(_remainingWoundsBinding.GetValue() - wounds);
        }

        public void SetPosition(Position newPosition)
        {
            PositionBinding.SetValue(newPosition);
        }

        #endregion

        [JsonConstructor]
        public ModelData(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules)
        {
            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds(specialRules);
        }

        public ModelData(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore)
        {
            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds(specialRules);

            _weapons = weapons;
            _specialRules = specialRules;

            _remainingWoundsRef = gameDataStore.Create(TotalWounds);
            _positionRef = gameDataStore.Create(initialPosition);

            SetGameDataStore(gameDataStore);
        }

        public ModelData(IModelTemplate modelToCopy, IReadWriteableGameDataStore gameDataStore)
        {
            BaseRadiusInches = modelToCopy.BaseRadiusInches;
            TotalWounds = CalculateTotalWounds(modelToCopy.SpecialRules);

            _weapons = new List<Weapon>(modelToCopy.Weapons);
            _specialRules = new List<SpecialRule>(modelToCopy.SpecialRules);

            _remainingWoundsRef = gameDataStore.Create(TotalWounds);
            _positionRef = gameDataStore.Create(new Position());

            SetGameDataStore(gameDataStore);
        }

        public void SetGameDataStore(IReadWriteableGameDataStore gameDataStore)
        {
            _remainingWoundsBinding = gameDataStore.GetDataBinding<float>(_remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(_positionRef);
        }

        //TODO: JSON constructor that turns DataReferences into DataBindings, which has to inject the GameDataStore and CommandProcessor.

        private int CalculateTotalWounds(IReadOnlyList<SpecialRule> specialRules)
        {
            //TODO: Get ones that modify total wounds somehow, and process.
            return 1;
        }


    }
}
