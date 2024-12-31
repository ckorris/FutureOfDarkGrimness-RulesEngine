using System;
using System.Collections.Generic;
using FDG.Data;
using System.Linq;

namespace FDG
{
    public struct Model : IModel
    {
        public readonly float BaseRadiusInches;

        public readonly float TotalWounds { get; }

        private DataReference _remainingWoundsRef;

        private readonly DataBinding<float> _remainingWoundsBinding;

        private DataReference _positionRef;

        private readonly DataBinding<Position> _positionBinding;

        public readonly List<Weapon> Weapons;

        public readonly List<SpecialRule> SpecialRules; //TODO: Not yet used on the interface, unit only.


        #region IModel Non-Serialized

        float IModel.WoundsDealt => TotalWounds - _remainingWoundsBinding.GetValue();

        Position IModel.Position => _positionBinding.GetValue();

        float IModel.BaseRadiusInches => BaseRadiusInches;

        List<IWeapon> IModel.Weapons => Weapons.Cast<IWeapon>().ToList();

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

        public Model(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore, ICommandProcessor commandProcessor)
        {
            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds(specialRules);

            _remainingWoundsRef = gameDataStore.Create(TotalWounds);
            _remainingWoundsBinding = new DataBinding<float>(commandProcessor, gameDataStore, _remainingWoundsRef);

            _positionRef = gameDataStore.Create(initialPosition);
            _positionBinding = new DataBinding<Position>(commandProcessor, gameDataStore, _positionRef);

            Weapons = weapons;
            SpecialRules = specialRules;
        }

        //TODO: JSON constructor that turns DataReferences into DataBindings, which has to inject the GameDataStore and CommandProcessor.

        private int CalculateTotalWounds(List<SpecialRule> specialRules)
        {
            //TODO: Get ones that modify total wounds somehow, and process.
            return 1;
        }

    }
}
