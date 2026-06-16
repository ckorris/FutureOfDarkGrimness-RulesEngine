namespace FDG
{
    /// <summary>
    /// Metadata a host <see cref="UnitData"/> carries once a Hero has joined it (#006). The hero's
    /// <see cref="ModelData"/> is physically merged into the host's model list — so targeting, line of
    /// sight, coherency, save rolls at the unit Defense, movement and activation all treat the combined
    /// unit as one with no further code — and this record carries the few facts that diverge from the
    /// rank and file: which model is the hero, and the hero's own Quality / Defense.
    ///
    /// Read by the stat-divergence seams:
    /// <list type="bullet">
    ///   <item>morale tests use <see cref="Quality"/> on behalf of the unit;</item>
    ///   <item>the hero saves at the unit's Defense until it is the sole survivor, then at
    ///         <see cref="Defense"/>;</item>
    ///   <item>the hero's weapon batches fire at <see cref="Quality"/>;</item>
    ///   <item>wounds are assigned to <see cref="HeroModelId"/> last.</item>
    /// </list>
    /// </summary>
    public class HeroAttachment
    {
        public ModelID HeroModelId { get; set; }

        /// <summary> The hero model's own Quality (the rank and file keep the host unit's Quality). </summary>
        public int Quality { get; set; }

        /// <summary> The hero model's own Defense, used only once the hero is the last living model. </summary>
        public int Defense { get; set; }

        /// <summary>
        /// The hero model's own max wounds (its Tough value, or 1). Captured at merge because the hero's
        /// standalone unit is never registered, so the creation-rules pass that normally applies Tough
        /// never runs for it; <see cref="Rules.Dispatch.UnitCreationRules"/> applies this to the hero
        /// model instead of the host unit's Tough.
        /// </summary>
        public int HeroWounds { get; set; }

        public HeroAttachment()
        {
        }

        public HeroAttachment(ModelID heroModelId, int quality, int defense, int heroWounds)
        {
            HeroModelId = heroModelId;
            Quality = quality;
            Defense = defense;
            HeroWounds = heroWounds;
        }
    }
}
