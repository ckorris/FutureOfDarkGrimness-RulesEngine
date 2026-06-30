using FDG.BuiltInAssets;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
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

        // #095: persisted form of RuleDefinitions (which is [JsonIgnore]). See RuleAttachmentPersistence.
        [JsonProperty] private string? _ruleDefinitionsJson;

        /// <summary>
        /// Attaches a resolved special-rule definition to this model. Post-construction (army-load / the
        /// hero merge / harness), mirroring <see cref="UnitData.AttachRuleDefinition"/>.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule)
        {
            _ruleDefinitions.Add(rule);
            _ruleDefinitionsJson = RuleAttachmentPersistence.Serialize(_ruleDefinitions);
        }

        /// <summary>
        /// #095: replays the persisted rule blob back onto <see cref="RuleDefinitions"/> after a load,
        /// mirroring <see cref="UnitData.RehydrateRules"/>. Idempotent; no-op unless the live list is empty.
        /// </summary>
        public void RehydrateRules()
        {
            if (_ruleDefinitions.Count == 0 && !string.IsNullOrEmpty(_ruleDefinitionsJson))
            {
                _ruleDefinitions.AddRange(RuleAttachmentPersistence.Deserialize(_ruleDefinitionsJson));
            }
        }

        // Serialized: max wounds is set imperatively after creation (e.g. Tough via UnitCreationRules),
        // so it can't be recomputed on load — it must round-trip, or a loaded/networked Tough model
        // would revert to 1.
        public float TotalWounds { get; private set; }

        public DataBinding<float> RemainingWoundsBinding;

        public DataBinding<Position> PositionBinding;

        // Per-model yaw facing as a unit normal (#150). Store-backed like PositionBinding so it replicates
        // over the network, is observable for rendering, and round-trips through save/load.
        public DataBinding<Float2> FacingBinding;

        public List<Weapon> Weapons;


        #region IModel Non-Serialized

        public float WoundsDealt => TotalWounds - RemainingWoundsBinding.GetValue();

        public Position Position => PositionBinding.GetValue();

        /// <summary> The facing every freshly created model gets: +Z, reproducing the pre-#150 axis-aligned layout. </summary>
        public static readonly Float2 DefaultFacing = new Float2(0f, 1f);

        public Float2 Facing => FacingBinding.GetValue();

        // The base footprint (#149). Serialized (Newtonsoft TypeNameHandling.Auto records the concrete
        // shape via $type); legacy saves with no base fall back to the default circle in the ctor.
        public IBaseShape BaseShape { get; }

        [JsonIgnore] public float BaseRadiusInches => BaseShape.BoundingRadiusInches;


        IReadOnlyList<Weapon> IModel.Weapons => Weapons;


        event DataValueChangedHandler<Position> IModel.OnPositionChanged
        {
            add { PositionBinding.OnValueChanged += value; }
            remove { PositionBinding.OnValueChanged -= value; }
        }

        event DataValueChangedHandler<Float2> IModel.OnFacingChanged
        {
            add { FacingBinding.OnValueChanged += value; }
            remove { FacingBinding.OnValueChanged -= value; }
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

        public void SetFacing(Float2 facingNormal)
        {
            FacingBinding.SetValue(facingNormal);
        }

        #endregion

        [JsonConstructor]
        public ModelData(IBaseShape baseShape, float totalWounds, DataBinding<float> remainingWoundsBinding, DataBinding<Position> positionBinding,
            DataBinding<Float2> facingBinding, List<Weapon> weapons, ModelID? id = null)
        {
            ID = id ?? new ModelID(System.Guid.NewGuid());

            // A legacy save predating #149 has no base → default circle (28mm), the value it used anyway.
            BaseShape = baseShape ?? BaseShapeDefaults.Default();
            RemainingWoundsBinding = remainingWoundsBinding;
            PositionBinding = positionBinding;
            // Saves predating #150 carry no facing binding; such saves don't survive a version anyway (#070).
            FacingBinding = facingBinding;
            Weapons = weapons;
            TotalWounds = totalWounds;
        }

        // Convenience overload: a circular base of the given radius (keeps existing callers/tests working).
        public ModelData(float baseRadiusInches, List<Weapon> weapons, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore)
            : this(new CircleBase(baseRadiusInches), weapons, initialPosition, gameDataStore) { }

        public ModelData(IBaseShape baseShape, List<Weapon> weapons, Position initialPosition,
            IReadWriteableGameDataStore gameDataStore)
        {
            ID = new ModelID(System.Guid.NewGuid());

            BaseShape = baseShape;
            TotalWounds = CalculateTotalWounds();

            Weapons = weapons;

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(initialPosition);
            DataReference facingRef = gameDataStore.Create(DefaultFacing);

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
            FacingBinding = gameDataStore.GetDataBinding<Float2>(facingRef);
        }

        public ModelData(IModelTemplate modelToCopy, IReadWriteableGameDataStore gameDataStore)
        {
            ID = new ModelID(System.Guid.NewGuid());

            BaseShape = modelToCopy.BaseShape;
            TotalWounds = CalculateTotalWounds();

            Weapons = new List<Weapon>(modelToCopy.Weapons);

            DataReference remainingWoundsRef = gameDataStore.Create(TotalWounds);
            DataReference positionRef = gameDataStore.Create(new Position());
            DataReference facingRef = gameDataStore.Create(DefaultFacing);

            RemainingWoundsBinding = gameDataStore.GetDataBinding<float>(remainingWoundsRef);
            PositionBinding = gameDataStore.GetDataBinding<Position>(positionRef);
            FacingBinding = gameDataStore.GetDataBinding<Float2>(facingRef);
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
                otherModel.PositionBinding.GetValue(), thisModel.BaseShape, otherModel.BaseShape);
        }

        public static float BaseDistanceToOtherModel_3D(this ModelData thisModel, ModelData otherModel)
        {
            return DistanceUtilities.GetBaseToBaseDistanceInches_3D(thisModel.PositionBinding.GetValue(),
                otherModel.PositionBinding.GetValue(), thisModel.BaseShape, otherModel.BaseShape);
        }
    }
}
