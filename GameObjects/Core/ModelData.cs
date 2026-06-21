using FDG.BuiltInAssets;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
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
        public ModelID ID { get; private set; }

        [JsonProperty] private TokenContainer _tokens = new TokenContainer();

        [JsonIgnore] public ITokenContainer Tokens => _tokens;

        // #006 slice F: per-model rules. Like UnitData/Weapon rule lists, deliberately [JsonIgnore] —
        // re-attached at army-load (the hero merge moves the hero's unit rules onto its model).
        private readonly List<ResolvedRule> _ruleDefinitions = new();

        [JsonIgnore] public IReadOnlyList<ResolvedRule> RuleDefinitions => _ruleDefinitions;

        /// <summary>
        /// Attaches a resolved special-rule definition to this model. Post-construction (army-load / the
        /// hero merge / harness), mirroring <see cref="UnitData.AttachRuleDefinition"/>.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule) => _ruleDefinitions.Add(rule);

        // Serialized: max wounds is set imperatively after creation (e.g. Tough via UnitCreationRules),
        // so it can't be recomputed on load — it must round-trip, or a loaded/networked Tough model
        // would revert to 1.
        public float TotalWounds { get; private set; }

        public DataBinding<float> RemainingWoundsBinding;

        public DataBinding<Position> PositionBinding;

        public List<Weapon> Weapons;


        #region IModel Non-Serialized

        public float WoundsDealt => TotalWounds - RemainingWoundsBinding.GetValue();

        public Position Position => PositionBinding.GetValue();

        public float BaseRadiusInches { get; }


        IReadOnlyList<Weapon> IModel.Weapons => Weapons;


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

        public void SetMaxWounds(int maxWounds)
        {
            TotalWounds = maxWounds;
            RemainingWoundsBinding.SetValue(maxWounds);
        }

        public void SetPosition(Position newPosition)
        {
            PositionBinding.SetValue(newPosition);
        }

        #endregion

        [JsonConstructor]
        public ModelData(float baseRadiusInches, float totalWounds, DataBinding<float> remainingWoundsBinding, DataBinding<Position> positionBinding,
            List<Weapon> weapons, ModelID? id = null)
        {
            ID = id ?? new ModelID(System.Guid.NewGuid());

            BaseRadiusInches = baseRadiusInches;
            RemainingWoundsBinding = remainingWoundsBinding;
            PositionBinding = positionBinding;
            Weapons = weapons;
            TotalWounds = totalWounds;
        }

        public ModelData(float baseRadiusInches, List<Weapon> weapons, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore)
        {
            ID = new ModelID(System.Guid.NewGuid());

            BaseRadiusInches = baseRadiusInches;
            TotalWounds = CalculateTotalWounds();

            Weapons = weapons;

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(initialPosition);

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
        }

        public ModelData(IModelTemplate modelToCopy, IReadWriteableGameDataStore gameDataStore)
        {
            ID = new ModelID(System.Guid.NewGuid());

            BaseRadiusInches = modelToCopy.BaseRadiusInches;
            TotalWounds = CalculateTotalWounds();

            Weapons = new List<Weapon>(modelToCopy.Weapons);

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(new Position());

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
        }

        private int CalculateTotalWounds()
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
