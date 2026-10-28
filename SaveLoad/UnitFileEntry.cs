
namespace FDG.SaveLoad
{
    [Serializable]
    public class UnitFileEntry
    {
        // #191: Interlocked, not `_nextID++`. Army files are deserialized concurrently by the lab
        // (games run in parallel) and by the search; a non-atomic increment hands two entries the
        // same StableID, which is the key other data points at.
        public int StableID { get; } = System.Threading.Interlocked.Increment(ref _nextID) - 1;

        private static int _nextID = 1;

        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Optional author-stable handle for this unit within its army file, used as the target of a
        /// Hero's <see cref="JoinsUnitId"/> (#006). Distinct from <see cref="StableID"/>, which is a
        /// per-process counter and is never serialized, so it can't be a cross-file reference.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// When this entry is a Hero (carries the "Hero" special rule), names the <see cref="Id"/> of the
        /// friendly multi-model unit it joins at army setup (#006). Null = the hero deploys on its own.
        /// </summary>
        public string? JoinsUnitId { get; set; }

        public int ModelCount { get; set; }

        /// <summary>
        /// The base footprint shared by all this unit's models (#149). Defaults to a 28mm circle, so a
        /// pre-#149 army file (no <c>base</c> property) is unchanged.
        /// </summary>
        public BaseFileEntry Base { get; set; } = new BaseFileEntry();

        public int Quality { get; set; }

        public int Defense { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();

        public List<WeaponFileEntry> Weapons { get; set; } = new List<WeaponFileEntry>();

        /// <summary>
        /// #407: rules only some of the unit's models have (a one-model Weapon Team's Tough(3)). Absent
        /// on almost every unit, so null unless a grant exists and omitted on save - older army files, and
        /// every unit without one, round-trip unchanged.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<ModelGrantEntry>? ModelGrants { get; set; }

        public int PointCost { get; set; }
    }
}
