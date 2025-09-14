using FDG.BuiltInAssets;
using FDG.Data;
using FDG.SerializableVisuals;
using FDG.SerializableVisuals.Materials;
using FDG.SerializableVisuals.Meshes;
using FDG.SerializableVisuals.Textures;
using Newtonsoft.Json;
using System.Numerics;


namespace FDG
{
    public class ModelData : IModel
    {

        [JsonIgnore]
        public float TotalWounds { get; }

        public DataBinding<float> RemainingWoundsBinding;

        public DataBinding<Position> PositionBinding;

        public List<Weapon> Weapons;

        public List<SpecialRule> SpecialRules;


        #region IModel Non-Serialized

        public float WoundsDealt => TotalWounds - RemainingWoundsBinding.GetValue();

        public Position Position => PositionBinding.GetValue();

        public float BaseRadiusInches { get; }


        IReadOnlyList<Weapon> IModel.Weapons => Weapons;

        IReadOnlyList<SpecialRule> IModel.SpecialRules => SpecialRules;


        event DataValueChangedHandler<Position> IModel.OnPositionChanged
        {
            add { PositionBinding.OnValueChanged += value; }
            remove { PositionBinding.OnValueChanged -= value; }
        }

        event DataValueChangedHandler<float> IModel.OnWoundsDealt
        {
            add { RemainingWoundsBinding.OnValueChanged += value; }
            remove { RemainingWoundsBinding.OnValueChanged -= value; }
        }


        #region Visuals
        [JsonIgnore] //Only while it's a default.
        public IMeshProvider MeshProvider => new BuiltInObjMeshProvider(BuiltInAssetHelper.SILLYMANMODEL_PATH); //TEMP default.

        [JsonIgnore] //Only while it's a default.
        public IMaterialProvider MaterialProvider
        {
            get
            {
                //TEMP default.
                var textureProvider = new BuiltInPngTextureProvider(BuiltInAssetHelper.SILLYMANTEXTURE_PATH);

                var materialProvider = new BasicMaterial();
                materialProvider.BaseColor = new Vector4(1, 1, 1, 1);
                materialProvider.BaseColorTexture = textureProvider;

                return materialProvider;
            }
        }

        float IModel.WoundsDealt => WoundsDealt;

        Position IModel.Position => Position;

        #endregion


        public void DealWounds(float wounds)
        {
            RemainingWoundsBinding.SetValue(RemainingWoundsBinding.GetValue() - wounds);
        }

        public void SetPosition(Position newPosition)
        {
            PositionBinding.SetValue(newPosition);
        }

        #endregion

        [JsonConstructor]
        public ModelData(float baseRadiusInches, DataBinding<float> remainingWoundsBinding, DataBinding<Position> positionBinding, 
            List<Weapon> weapons, List<SpecialRule> specialRules)
        {
            BaseRadiusInches = baseRadiusInches;
            RemainingWoundsBinding = remainingWoundsBinding;
            PositionBinding = positionBinding;
            Weapons = weapons;
            SpecialRules = specialRules;
            TotalWounds = CalculateTotalWounds(specialRules);
        }

        public ModelData(float baseRadiusInches, List<Weapon> weapons, List<SpecialRule> specialRules, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore)
        {
            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds(specialRules);

            Weapons = weapons;
            SpecialRules = specialRules;

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(initialPosition);

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
        }

        public ModelData(IModelTemplate modelToCopy, IReadWriteableGameDataStore gameDataStore)
        {
            BaseRadiusInches = modelToCopy.BaseRadiusInches;
            TotalWounds = CalculateTotalWounds(modelToCopy.SpecialRules);

            Weapons = new List<Weapon>(modelToCopy.Weapons);
            SpecialRules = new List<SpecialRule>(modelToCopy.SpecialRules);

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(new Position());

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
        }

        private int CalculateTotalWounds(IReadOnlyList<SpecialRule> specialRules)
        {
            //TODO: Get ones that modify total wounds somehow, and process.
            return 1;
        }
    }

    public static class ModelDataExtensions
    {
        public static bool GetIsAlive(this ModelData model)
        {
            return model.WoundsDealt < model.TotalWounds;
        }

        public static bool GetIsDead(this ModelData model)
        {
            return model.WoundsDealt >= model.TotalWounds;
        }

        public static float BaseDistanceToOtherModel_2D(this ModelData thisModel, ModelData otherModel)
        {
            return DistanceUtilities.GetBaseToBaseDistanceInches_2D(thisModel.PositionBinding.GetValue(), 
                otherModel.PositionBinding.GetValue(),thisModel.BaseRadiusInches, otherModel.BaseRadiusInches);
        }

        public static float BaseDistanceToOtherModel_3D(this ModelData thisModel, ModelData otherModel)
        {
            return DistanceUtilities.GetBaseToBaseDistanceInches_3D(thisModel.PositionBinding.GetValue(), 
                otherModel.PositionBinding.GetValue(),thisModel.BaseRadiusInches, otherModel.BaseRadiusInches);
        }
    }
}
