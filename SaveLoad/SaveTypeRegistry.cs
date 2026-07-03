using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.SaveLoad
{
    /// <summary>
    /// The single source of truth mapping engine types to <b>stable string IDs</b> for the save format
    /// (#070), so renaming or moving a C# type no longer breaks existing saves. Before this, both the save
    /// file's type-map fingerprint and every polymorphic <c>$type</c> inside store JSON recorded a raw
    /// <see cref="Type.FullName"/>; a rename silently invalidated every save.
    ///
    /// <para>Two consumers share this registry:</para>
    /// <list type="bullet">
    ///   <item><see cref="GameSaveSerializer"/> writes an ID (not the FullName) for each registered store
    ///   type in the save's type map, and resolves it back on load.</item>
    ///   <item><see cref="StableTypeSerializationBinder"/> emits/reads an ID for the polymorphic
    ///   <c>$type</c> payloads Newtonsoft records under <c>TypeNameHandling.Auto</c> inside store entries
    ///   (base shapes, token payloads, token clear-triggers, terrain zones).</item>
    /// </list>
    ///
    /// <para>Anything not registered here falls back to <see cref="Type.FullName"/> (the pre-#070 behavior),
    /// so an omission degrades to today's fragility, never to a load failure or data loss. The
    /// <c>SaveTypeRegistryTests</c> reflect over every persisted polymorphic family and every registered
    /// store type and assert each has an ID, so a new base shape / payload / clear-trigger / zone added
    /// without an ID fails CI.</para>
    ///
    /// <para><b>IDs are permanent.</b> Once shipped, an ID string must never be renamed — that would
    /// reintroduce the exact breakage this class exists to prevent. Add new entries; never rename existing
    /// ones. (Not relevant yet — nothing is distributed — but the contract holds from here on.)</para>
    /// </summary>
    public static class SaveTypeRegistry
    {
        private static readonly Dictionary<Type, string> _idByType = new Dictionary<Type, string>();
        private static readonly Dictionary<string, Type> _typeById = new Dictionary<string, Type>();

        static SaveTypeRegistry()
        {
            // --- Registered store types (the save's top-level type map). Their positional TypeID is
            //     unchanged; only the string identity written for each entry moves from FullName to ID. ---
            Add(GameDataStore.PlaceholderType, "__placeholder");
            Add(typeof(int), "int");
            Add(typeof(float), "float");
            Add(typeof(string), "string");
            Add(typeof(Position), "position");
            Add(typeof(ModelData), "model");
            Add(typeof(PlayerSlotInfo), "playerSlot");
            Add(typeof(TeamData), "team");
            Add(typeof(UnitData), "unit");
            Add(typeof(ArmyData), "army");
            Add(typeof(TerrainData), "terrain");
            Add(typeof(RectangularZone), "rectangularZone");
            Add(typeof(ObjectiveData), "objective");
            Add(typeof(PlayerID), "playerId");
            Add(typeof(GameProgressData), "gameProgress");
            Add(typeof(Float2), "float2");

            // --- Polymorphic $type payloads Newtonsoft emits inside store entries (TypeNameHandling.Auto). ---

            // IBaseShape — ModelData.BaseShape (#149/#150).
            Add(typeof(CircleBase), "baseShape.circle");
            Add(typeof(RectangleBase), "baseShape.rect");

            // IZone — TerrainData.Shape, and nested inside CompositeZone/RotatedZoneWrapper. RectangularZone
            // is already registered above (it is also a top-level store type); its one ID serves both roles.
            Add(typeof(CircularZone), "zone.circular");
            Add(typeof(RotatedZoneWrapper), "zone.rotated");
            Add(typeof(CompositeZone), "zone.composite");
            // CompositeZone.Parts is IReadOnlyList<IZone> but always a List<IZone> at runtime; under
            // TypeNameHandling.Auto Newtonsoft records the collection's concrete type too, so register it as
            // well — otherwise that wrapper's $type stays the assembly-qualified "List`1[[FDG.IZone, ...]]".
            Add(typeof(List<IZone>), "zone.list");

            // TokenPayload — Token.Payload (the closed sum type of structured token data).
            Add(typeof(TokenPayload.RuleGrant), "tokenPayload.ruleGrant");
            Add(typeof(TokenPayload.StatModifier), "tokenPayload.statModifier");
            Add(typeof(TokenPayload.WeaponName), "tokenPayload.weaponName");

            // TokenClearTrigger — Token.ClearTrigger. Non-nullable, so EVERY saved token carries a $type here.
            Add(typeof(TokenClearTrigger.ManualOnly), "clearTrigger.manualOnly");
            Add(typeof(TokenClearTrigger.RoundEnd), "clearTrigger.roundEnd");
            Add(typeof(TokenClearTrigger.ActivationEnd), "clearTrigger.activationEnd");
            Add(typeof(TokenClearTrigger.AttackEnd), "clearTrigger.attackEnd");
            Add(typeof(TokenClearTrigger.FirstTrigger), "clearTrigger.firstTrigger");
            Add(typeof(TokenClearTrigger.UnitDestroyed), "clearTrigger.unitDestroyed");
            Add(typeof(TokenClearTrigger.OwnerDestroyed), "clearTrigger.ownerDestroyed");
            Add(typeof(TokenClearTrigger.CustomHook), "clearTrigger.customHook");
        }

        private static void Add(Type type, string id)
        {
            // Both throw on a duplicate — a duplicate type or a reused ID is a programming error, caught at
            // first touch (the static ctor runs before any save/load).
            _idByType.Add(type, id);
            _typeById.Add(id, type);
        }

        /// <summary>The stable ID for <paramref name="type"/>, or false if it isn't registered.</summary>
        public static bool TryGetId(Type type, out string id) => _idByType.TryGetValue(type, out id!);

        /// <summary>The type for a stable <paramref name="id"/>, or false if the string isn't a known ID.</summary>
        public static bool TryGetType(string id, out Type type) => _typeById.TryGetValue(id, out type!);

        /// <summary>
        /// The stable ID for <paramref name="type"/>, falling back to its <see cref="Type.FullName"/> when
        /// unregistered — so the save format is uniform (IDs where we have them) and forward-compatible
        /// (an unregistered type still serializes, just without rename-safety).
        /// </summary>
        public static string GetIdOrFullName(Type type) =>
            _idByType.TryGetValue(type, out string? id) ? id : type.FullName!;
    }
}
