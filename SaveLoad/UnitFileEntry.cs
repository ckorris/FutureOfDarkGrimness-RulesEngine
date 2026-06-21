
namespace FDG.SaveLoad
{
    [Serializable]
    public class UnitFileEntry
    {
        public int StableID { get; } = _nextID++;

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

        public int Quality { get; set; }

        public int Defense { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();

        public List<WeaponFileEntry> Weapons { get; set; } = new List<WeaponFileEntry>();

        public int PointCost { get; set; }
    }
}
