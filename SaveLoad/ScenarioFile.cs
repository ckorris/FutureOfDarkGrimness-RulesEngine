namespace FDG.SaveLoad
{
    /// <summary>
    /// The authoring format for a test scenario (#167): a compact JSON description of a game state —
    /// armies, placements, pre-applied wounds/tokens, and whose activation it is — that
    /// <see cref="ScenarioCompiler"/> turns into a resumable <c>.fdgsave</c>. Serialized with
    /// <see cref="FDG.Rules.Serialization.RuleJson.Options"/> (camelCase), the same convention as
    /// army files.
    /// </summary>
    public class ScenarioFile
    {
        public const string EXTENSION_NO_PERIOD = "json";

        public string Name { get; set; } = string.Empty;

        /// <summary>1-based round number the scenario starts in (1..NUMBER_OF_ROUNDS).</summary>
        public int Round { get; set; } = 1;

        /// <summary>Index into <see cref="Players"/> of the player whose activation comes next.</summary>
        public int ActivePlayer { get; set; } = 0;

        public ScenarioSettings Settings { get; set; } = new ScenarioSettings();

        /// <summary>
        /// Objective marker positions as [x, z] pairs. Null/absent = three markers spread across the
        /// table midline (objectives decide the winner, so every scenario gets some).
        /// </summary>
        public List<float[]>? Objectives { get; set; }

        /// <summary>
        /// Terrain pieces placed on the table before the game resumes. Null/absent = open table
        /// (the pre-#264 behavior). Auto-placed units (those without explicit model positions) are
        /// rowed WITHOUT terrain awareness - place the units a terrain-sensitive scenario cares
        /// about explicitly.
        /// </summary>
        public List<ScenarioTerrain>? Terrain { get; set; }

        public List<ScenarioPlayer> Players { get; set; } = new List<ScenarioPlayer>();
    }

    /// <summary>
    /// One terrain piece: a shape (axis-aligned rectangle, optionally rotated, or a circle) plus the
    /// engine's <see cref="ETerrainType"/> flags. Compiled through the same construction the live
    /// PlaceTerrainStage uses (centered shape, rotation wrapped around the AABB center), so a
    /// scenario piece behaves exactly like a player-placed one.
    /// </summary>
    public class ScenarioTerrain
    {
        /// <summary>
        /// <see cref="ETerrainType"/> flag names, combined with '|' or ',' (case-insensitive):
        /// e.g. "Impassible", "Blocking|Impassible", "Cover, Difficult".
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>"Rectangle" (or "Rect") or "Circle", case-insensitive.</summary>
        public string Shape { get; set; } = string.Empty;

        /// <summary>The piece's center as [x, z], in table inches.</summary>
        public float[]? Center { get; set; }

        /// <summary>Rectangle only: [width (x-extent), depth (z-extent)] in inches, both > 0.</summary>
        public float[]? Size { get; set; }

        /// <summary>Circle only: diameter in inches, > 0 (matches the army files' base convention).</summary>
        public float? Diameter { get; set; }

        /// <summary>
        /// Rectangle only: rotation around the piece's center in degrees, same convention as the
        /// in-game terrain placement dial. Rejected on circles (meaningless there) rather than
        /// silently ignored.
        /// </summary>
        public float RotationDegrees { get; set; }

        /// <summary>Piece height in inches (defaults to 0; sight-line rules currently ignore it).</summary>
        public float HeightInches { get; set; }
    }

    public class ScenarioSettings
    {
        /// <summary>"Probabilistic" (default — histogram dice, deterministic modifier arithmetic) or "Realistic".</summary>
        public string Randomness { get; set; } = "Probabilistic";

        /// <summary>
        /// Optional seed for repeatable runs. Seeds Realistic dice, and (since #193) Probabilistic mode's
        /// decisive rolls, the objective auto-placer, and each AI player's own stream — so a seeded
        /// scenario replays identically in either randomness mode.
        /// </summary>
        public int? DiceSeed { get; set; }

        /// <summary>
        /// Cosmetic table surface (#265): Forest (default), Desert, Ice, MarsLike, Urban, Barren.
        /// Matches the lobby's Battlefield dropdown; no rule reads it.
        /// </summary>
        public string? Background { get; set; }
    }

    public class ScenarioPlayer
    {
        /// <summary>Path to this player's .fdgarmy file, resolved relative to the scenario file.</summary>
        public string Army { get; set; } = string.Empty;

        /// <summary>Team number. Absent = this player's index (everyone on their own team).</summary>
        public int? Team { get; set; }

        /// <summary>
        /// Per-unit setup. Units not mentioned here still deploy — auto-placed in a row inside their
        /// team's deployment band — so only the units the scenario cares about need entries.
        /// </summary>
        public List<ScenarioUnit> Units { get; set; } = new List<ScenarioUnit>();
    }

    public class ScenarioUnit
    {
        /// <summary>
        /// The unit's name in the army file. Matched after hero joins resolve, so a joined hero's
        /// models belong to its HOST unit's entry. Use <see cref="UnitIndex"/> when names repeat.
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>Index of the unit within the player's army (post hero-join), when names are ambiguous.</summary>
        public int? UnitIndex { get; set; }

        /// <summary>
        /// One [x, z] position per model, in the unit's model order (host models first, then a joined
        /// hero's). When present, the count must equal the unit's model count. (0,0) is reserved for
        /// "not on the table" and is rejected.
        /// </summary>
        public List<float[]>? Models { get; set; }

        /// <summary>
        /// Optional facing normal [x, z] applied to all the unit's models. Absent = face the table
        /// center line (+Z from the near half, -Z from the far half).
        /// </summary>
        public float[]? Facing { get; set; }

        /// <summary>
        /// Pre-applied wounds per model, aligned with <see cref="Models"/> order. Shorter than the
        /// model count = remaining models take 0. Applied after creation rules, so Tough totals are
        /// respected; wounding a model to 0 starts it dead.
        /// </summary>
        public List<float>? WoundsDealt { get; set; }

        /// <summary>True = this unit has already activated this round.</summary>
        public bool Activated { get; set; }

        /// <summary>Unit-level tokens to pre-apply (Shaken, Fatigued, SpellTokens, ...).</summary>
        public List<ScenarioToken>? Tokens { get; set; }
    }

    public class ScenarioToken
    {
        /// <summary>The engine token-type ID (e.g. "Shaken", "Fatigued", "SpellTokens"). Case-insensitive for known types.</summary>
        public string Type { get; set; } = string.Empty;

        public int Count { get; set; } = 1;

        /// <summary>
        /// Clear-trigger name: ManualOnly (default), RoundEnd, ActivationEnd, AttackEnd, FirstTrigger,
        /// UnitDestroyed, OwnerDestroyed.
        /// </summary>
        public string ClearTrigger { get; set; } = "ManualOnly";

        /// <summary>
        /// Optional signed delta for the granted roll-modifier token types (HitRollModifier,
        /// SaveRollModifier, MoraleRollModifier, CastRollModifier). Without it the token carries no
        /// payload and the roll stages read a net of zero - so a scenario could place the token but
        /// never see it apply. Rejected on token types that carry no <c>StatModifier</c> payload,
        /// rather than being silently dropped.
        /// </summary>
        public int? Delta { get; set; }
    }
}
