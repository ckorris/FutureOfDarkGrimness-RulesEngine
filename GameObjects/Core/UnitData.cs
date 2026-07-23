using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
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

        // #029/#150: an Aircraft's fixed flight heading is no longer a unit field — it lives on the models'
        // per-model IModel.Facing (set once toward the table centre, never turned), read back via
        // ForcedAircraftMove.EnsureHeading and gated by the AircraftHeadingSet token.

        private readonly List<ResolvedRule> _ruleDefinitions = new();

        [JsonIgnore] public IReadOnlyList<ResolvedRule> RuleDefinitions => _ruleDefinitions;

        // #095: RuleDefinitions is [JsonIgnore] (the resolved rule graph can't round-trip through the
        // Newtonsoft store/save layer), so its persisted form is this STJ blob, kept current in
        // AttachRuleDefinition and replayed by RehydrateRules on load. See RuleAttachmentPersistence.
        [JsonProperty] private string? _ruleDefinitionsJson;

        // #006: set when a Hero has joined this unit. The hero's model is merged into ModelBindings;
        // this carries the divergent facts (which model, the hero's own Quality/Defense). Serialized so
        // the merge survives save/network round-trips alongside the merged ModelBindings.
        [JsonProperty] private HeroAttachment? _heroAttachment;

        [JsonIgnore] public HeroAttachment? HeroAttachment => _heroAttachment;

        [JsonIgnore] public ModelID? JoinedHeroModelId => _heroAttachment?.HeroModelId;

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

        [JsonConstructor]
        public UnitData(PlayerID playerID, string name, int quality, int defense,
            List<DataBinding<ModelData>> modelBindings,
            UnitID? id = null)
        {
            ID = id ?? new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = name;
            Quality = quality;
            Defense = defense;

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
        }

        public UnitData(PlayerID playerID, UnitFileEntry unitFileEntry, IReadWriteableGameDataStore gameDataStore,
            IRuleResolver? ruleResolver = null,
            string? defaultRangedEffectSet = null, string? defaultMeleeEffectSet = null)
        {
            ID = new UnitID(System.Guid.NewGuid());

            PlayerID = playerID;
            Name = unitFileEntry.Name;
            Quality = unitFileEntry.Quality;
            Defense = unitFileEntry.Defense;

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

                //Resolved once per entry and shared across the quantity's instances —
                //ResolvedRule is immutable, so duplicates of the same weapon can share it.
                List<ResolvedRule> weaponRuleDefinitions = ResolveWeaponRules(weaponEntry, ruleResolver);

                // #239: the entry's explicit effect-set key, else the army default for the weapon's
                // kind. Resolved once here so beat emission just reads Weapon.EffectKey.
                string? effectKey = weaponEntry.EffectSet
                    ?? (weaponEntry.RangeInches > 0 ? defaultRangedEffectSet : defaultMeleeEffectSet);

                for (int q = 0; q < weaponEntry.Quantity; q++)
                {
                    Weapon weapon = new Weapon(weaponEntry.Name, weaponEntry.RangeInches, weaponEntry.Attacks,
                            weaponEntry.ArmorPenetration, effectKey);

                    foreach (ResolvedRule rule in weaponRuleDefinitions)
                    {
                        weapon.AttachRuleDefinition(rule);
                    }

                    unitWeapons.Add(weapon);
                }
            }

            // The unit's base shape (#149), authored per-unit in the army file; all its models share it.
            // Immutable, so one instance is safely shared. Null (e.g. "base": null) → the default circle.
            IBaseShape baseShape = (unitFileEntry.Base ?? new SaveLoad.BaseFileEntry()).ToBaseShape();

            for (int i = 0; i < unitFileEntry.ModelCount; i++)
            {
                List<Weapon> modelWeapons = new List<Weapon>();

                for (int w = i; w < unitWeapons.Count; w += unitFileEntry.ModelCount)
                {
                    modelWeapons.Add(unitWeapons[w]);
                }

                ModelData modelData = new ModelData(baseShape, modelWeapons, new Position(), gameDataStore);

                DataReference modelReference = gameDataStore.Create(modelData);
                DataBinding<ModelData> modelBinding = gameDataStore.GetDataBinding<ModelData>(modelReference);
                ModelBindings.Add(modelBinding);
            }
        }

        /// <summary>
        /// Resolves a weapon file entry's special-rule names into attachable #042 definitions
        /// (#027). Scope-enforced: a unit-scoped rule named on a weapon is skipped with a
        /// warning. A null resolver (e.g. template-copy construction paths that never see an
        /// army file) yields no rules.
        /// </summary>
        private List<ResolvedRule> ResolveWeaponRules(WeaponFileEntry weaponEntry, IRuleResolver? ruleResolver)
        {
            List<ResolvedRule> resolved = new List<ResolvedRule>();

            if (ruleResolver == null)
            {
                return resolved;
            }

            foreach (SpecialRuleEntry ruleEntry in weaponEntry.SpecialRules)
            {
                ResolvedRule? rule = ArmyListRuleResolution.ResolveForScope(
                    ruleResolver, ruleEntry, ERuleScope.Weapon, $"weapon '{weaponEntry.Name}' of unit '{Name}'");

                if (rule != null)
                {
                    resolved.Add(rule);
                }
            }

            return resolved;
        }


        /// <summary>
        /// Re-subscribes this unit's wound aggregation to its models' <see cref="IModel.OnWoundsDealt"/>.
        /// The <see cref="JsonConstructorAttribute"/> path deliberately does NOT do this: it must stay
        /// order-independent (a model may not be deserialized yet when the unit is, e.g. during network
        /// full-sync), so it never calls <c>GetValue()</c>. Call this once after a full save/load replay,
        /// when every referenced model is guaranteed present. Idempotent — removes any existing handler
        /// before re-adding.
        /// </summary>
        public void RewireModelWoundSubscriptions()
        {
            foreach (DataBinding<ModelData> modelBinding in ModelBindings)
            {
                IModel model = modelBinding.GetValue();
                model.OnWoundsDealt -= OnModelWoundsDealt;
                model.OnWoundsDealt += OnModelWoundsDealt;
            }
        }

        private void OnModelWoundsDealt(float oldWoundsCount, float newWoundsCount)
        {
            //This is called with an individual models' old and new wound count.
            //So to invoke the same for the unit, we get the new wound count, and do math
            //to find out the old.
            float woundsDealt = oldWoundsCount - newWoundsCount;

            // #263 backstop: no attack path should ever reach a unit that is off the table — wounds
            // landing on one mean a targeting filter upstream is missing (the class of bug where an
            // Ambush reserve's origin-sitting models got charged in round 1). Checked via the
            // off-table TOKENS, not GetIsOnBattlefield(): a unit whose last model just died reads as
            // off-battlefield by position and would warn on every ordinary kill. Warn, don't throw —
            // a live game should degrade loudly, not crash.
            if (woundsDealt > 0f
                && (ReserveRules.IsInReserve(this)
                    || Tokens.HasToken(TokenType.EmbarkedIn)
                    || Tokens.HasToken(TokenType.OffTableFromForcedMove)))
            {
                RuleDiagnostics.WarnOnce($"off-table-wounds-{ID}",
                    $"{Name} took {woundsDealt} wound(s) while off the battlefield (reserve/embarked/off-table). " +
                    "No attack should reach it there - this is an engine targeting bug.");
            }

            float newUnitTotalWounds = RemainingWounds;
            float oldUnitTotalWounds = newUnitTotalWounds + woundsDealt;

            OnWoundsDealt?.Invoke(oldUnitTotalWounds, newUnitTotalWounds);
        }

        /// <summary>
        /// Attaches a resolved special-rule definition to this unit. Attachment is
        /// post-construction (army-load / harness) rather than a constructor param so
        /// the existing constructors stay untouched.
        /// </summary>
        public void AttachRuleDefinition(ResolvedRule rule)
        {
            _ruleDefinitions.Add(rule);
            _ruleDefinitionsJson = RuleAttachmentPersistence.Serialize(_ruleDefinitions);
        }

        /// <summary>
        /// #095: replays the persisted rule blob back onto <see cref="RuleDefinitions"/> after a load,
        /// so a resumed game carries the same rules it was saved with. Idempotent and a no-op unless the
        /// live list is empty (a freshly deserialized carrier), so it never double-attaches.
        /// </summary>
        public void RehydrateRules()
        {
            if (_ruleDefinitions.Count == 0 && !string.IsNullOrEmpty(_ruleDefinitionsJson))
            {
                _ruleDefinitions.AddRange(RuleAttachmentPersistence.Deserialize(_ruleDefinitionsJson));
            }
        }

        /// <summary>
        /// Merges a Hero into this unit (#006): adds the hero's model binding(s) to this unit's model
        /// list (wiring wound aggregation), and records the <see cref="HeroAttachment"/>. After this the
        /// combined unit is one <see cref="IUnit"/> for all per-unit machinery; only the stat-divergence
        /// seams read <see cref="HeroAttachment"/>. The hero's standalone unit is discarded by the caller.
        /// </summary>
        public void AttachHero(HeroAttachment heroAttachment, IReadOnlyList<DataBinding<ModelData>> heroModelBindings)
        {
            // Add only — deliberately no OnWoundsDealt subscription here. Fresh-game units don't subscribe
            // their rank and file at construction (only the save/load path wires them, via
            // RewireModelWoundSubscriptions, which now sees the merged hero binding too); subscribing only
            // the hero here would make the unit's wound aggregation fire asymmetrically.
            foreach (DataBinding<ModelData> modelBinding in heroModelBindings)
            {
                ModelBindings.Add(modelBinding);
            }

            _heroAttachment = heroAttachment;
        }

        /// <summary> True if a Hero has joined this unit. </summary>
        [JsonIgnore] public bool HasHero => _heroAttachment != null;

        /// <summary>
        /// The joined hero's model, or null if no hero has joined (or its model is gone). Identity is by
        /// <see cref="HeroAttachment.HeroModelId"/>, stable across the model list as models die.
        /// </summary>
        public IModel? GetHeroModel()
        {
            if (_heroAttachment == null)
            {
                return null;
            }

            return Models.FirstOrDefault(model => model.ID == _heroAttachment.HeroModelId);
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
