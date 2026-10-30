namespace FDG.ArmyBuilding
{
    /// <summary>
    /// #430 - the OPR 3.6.0 optional "Force Org." limits, one row per game system. Core Rules v3.6.0:
    /// "up to 1 Hero per N pts, and up to 1+X copies of the same unit, where X is 1 per M pts in their
    /// army (combined units count as one). No single unit may be worth over 35% of total points, and
    /// armies may only have max. 1 unit per U pts, and max. 1 model or Tough per T pts." Grimdark Future
    /// and Age of Fantasy print different numbers (AoF is the GDF row scaled by 3/4 throughout, except
    /// the 35% share), and each system suggests its own game sizes.
    ///
    /// <para>Every allowance is a plain integer division of the army's points basis (see
    /// <see cref="SaveLoad.ForceOrgValidator"/> for what the basis is), so each cap has one number to
    /// test at and one over. The table is keyed by the army's <c>GameSystem</c> slug through
    /// <see cref="For(string)"/>: a null/absent slug is Grimdark Future (<see cref="GameSystems.Normalize"/>)
    /// and a slug neither constant names (an OPR custom-book army) reads as Grimdark Future too, since
    /// OPR publishes no third table.</para>
    /// </summary>
    public sealed record ForceOrgLimits(
        int PointsPerHero,
        int PointsPerExtraCopy,
        int MaxUnitSharePercent,
        int PointsPerUnit,
        int PointsPerModelOrTough,
        int SuggestedStartPoints,
        int SuggestedFullPoints)
    {
        /// <summary>GF Core Rules v3.6.0: 1 Hero per 500, X per 1000, 35%, 1 unit per 200, 1 model or Tough
        /// per 20; "we suggest 1000pts to start, and 2000pts for a full match".</summary>
        public static ForceOrgLimits GrimdarkFuture { get; } = new(500, 1000, 35, 200, 20, 1000, 2000);

        /// <summary>AoF Core Rules v3.6.0: 1 Hero per 375, X per 750, 35%, 1 unit per 150, 1 model or Tough
        /// per 15; "we suggest 750pts to start, and 1500pts for a full match".</summary>
        public static ForceOrgLimits AgeOfFantasy { get; } = new(375, 750, 35, 150, 15, 750, 1500);

        /// <summary>The row for a game-system slug (absent or unrecognized = Grimdark Future).</summary>
        public static ForceOrgLimits For(string? gameSystem) =>
            GameSystems.SameSystem(gameSystem, GameSystems.AgeOfFantasy) ? AgeOfFantasy : GrimdarkFuture;

        /// <summary>The row for a lobby's Army Source setting. "All" takes any system, so it reads as the
        /// default system, Grimdark Future - the same row a list with no system field gets.</summary>
        public static ForceOrgLimits For(EAllowedGameSystems allowed) =>
            allowed == EAllowedGameSystems.AgeOfFantasy ? AgeOfFantasy : GrimdarkFuture;

        /// <summary>The lobby form of <see cref="Retarget(int, ForceOrgLimits, ForceOrgLimits)"/>: the host
        /// changed the Army Source setting from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static int Retarget(int points, EAllowedGameSystems from, EAllowedGameSystems to) =>
            Retarget(points, For(from), For(to));

        /// <summary>Most Heroes allowed at <paramref name="points"/> (1 per <see cref="PointsPerHero"/>).</summary>
        public int MaxHeroes(int points) => points / PointsPerHero;

        /// <summary>Most copies of one unit allowed at <paramref name="points"/>: 1+X, X being 1 per
        /// <see cref="PointsPerExtraCopy"/>.</summary>
        public int MaxCopies(int points) => 1 + points / PointsPerExtraCopy;

        /// <summary>Most units allowed at <paramref name="points"/> (1 per <see cref="PointsPerUnit"/>).</summary>
        public int MaxUnits(int points) => points / PointsPerUnit;

        /// <summary>Most "model or Tough" points allowed at <paramref name="points"/>.</summary>
        public int MaxModelsOrTough(int points) => points / PointsPerModelOrTough;

        /// <summary>The largest cost a single unit may have at <paramref name="points"/> (the share is
        /// floored, which is exact for whole-point costs: a unit is over only when it costs more).</summary>
        public int MaxUnitPoints(int points) => points * MaxUnitSharePercent / 100;

        /// <summary>
        /// The lobby/Forge default-size helper: when <paramref name="points"/> is one of
        /// <paramref name="from"/>'s two suggested sizes, the matching size of <paramref name="to"/>
        /// (start for start, full for full); any other number is a hand-typed choice and is returned
        /// untouched.
        /// </summary>
        public static int Retarget(int points, ForceOrgLimits from, ForceOrgLimits to)
        {
            if (points == from.SuggestedStartPoints) return to.SuggestedStartPoints;
            if (points == from.SuggestedFullPoints) return to.SuggestedFullPoints;
            return points;
        }
    }
}
