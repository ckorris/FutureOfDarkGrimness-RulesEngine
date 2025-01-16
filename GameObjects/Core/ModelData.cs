
using FDG.Data;

namespace FDG
{
    public class ModelData : IModel
    {
        public readonly float BaseRadiusInches;

        public float TotalWounds { get; }

        private DataReference _remainingWoundsRef;

        private readonly DataBinding<float> _remainingWoundsBinding;

        private DataReference _positionRef;

        private readonly DataBinding<Position> _positionBinding;

        private List<Weapon> _weapons;

        private List<SpecialRule> _specialRules;


        #region IModel Non-Serialized

        float IModel.WoundsDealt => TotalWounds - _remainingWoundsBinding.GetValue();

        Position IModel.Position => _positionBinding.GetValue();

        float IModel.BaseRadiusInches => BaseRadiusInches;

        IReadOnlyList<Weapon> IModel.Weapons => _weapons;

        IReadOnlyList<SpecialRule> IModel.SpecialRules => _specialRules;

        event DataValueChangedHandler<Position> IModel.OnPositionChanged
        {
            add { _positionBinding.OnValueChanged += value; }
            remove { _positionBinding.OnValueChanged -= value; }
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
            _positionBinding.SetValue(newPosition);
        }

        #endregion

        public ModelData(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds(specialRules);

            _remainingWoundsRef = gameDataStore.Create(TotalWounds);
            _remainingWoundsBinding = new DataBinding<float>(commandProcessor, gameDataStore, _remainingWoundsRef);

            _positionRef = gameDataStore.Create(initialPosition);
            _positionBinding = new DataBinding<Position>(commandProcessor, gameDataStore, _positionRef);

            _weapons = weapons;
            _specialRules = specialRules;
        }

        public ModelData(IModelTemplate modelToCopy, IReadWriteableGameDataStore gameDataStore, 
            ICommandProcessor commandProcessor)
        {
            BaseRadiusInches = modelToCopy.BaseRadiusInches;
            TotalWounds = CalculateTotalWounds(modelToCopy.SpecialRules);

            _remainingWoundsRef = gameDataStore.Create(TotalWounds);
            _remainingWoundsBinding = new DataBinding<float>(commandProcessor, gameDataStore, _remainingWoundsRef);

            _positionRef = gameDataStore.Create(new Position());
            _positionBinding = new DataBinding<Position>(commandProcessor, gameDataStore, _positionRef);

            _weapons = new List<Weapon>(modelToCopy.Weapons);
            _specialRules = new List<SpecialRule>(modelToCopy.SpecialRules);
        }

        //TODO: JSON constructor that turns DataReferences into DataBindings, which has to inject the GameDataStore and CommandProcessor.

        private int CalculateTotalWounds(IReadOnlyList<SpecialRule> specialRules)
        {
            //TODO: Get ones that modify total wounds somehow, and process.
            return 1;
        }

    }
}
