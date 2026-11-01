namespace FDG
{
    /// <summary>
    /// #425: what Sight Blockers (<see cref="ETerrainType.Blocking"/> pieces, and the model blockers built as
    /// such) do to ONE shot's cover verdict (<c>CoverMajority</c>). Core Rules v3.6.0 Cover Terrain: "Units with
    /// most models fully inside Cover terrain or behind Sight Blockers ... get +1 to Defense rolls"; Indirect:
    /// "When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of
    /// sight, and ignores Cover from Sight Blockers."
    /// <para>
    /// The engine's base cover model predates the 3.6 wording: for an ordinary shot a Blocking piece BLOCKS the
    /// sight line - the model behind it is out of sight, not in cover (#425 D3, owner review). Only a shot that
    /// ignores line of sight can be resolved against a model behind a Sight Blocker at all, and for those the
    /// cover source matters, so the distinction is made exactly there. Derived per weapon by
    /// <c>SightRuleQueries.SightBlockerCoverFor</c>.
    /// </para>
    /// </summary>
    public enum ESightBlockerCover
    {
        /// <summary>An ordinary shot: a Blocking piece blocks the line and grants nothing; Cover terrain counts.</summary>
        BlocksSight = 0,

        /// <summary>
        /// The shot ignores line of sight but NOT cover from Sight Blockers (Quick Throw after moving): a Blocking
        /// piece on the line counts as cover ("behind Sight Blockers"), and Cover terrain still counts.
        /// </summary>
        GrantsCover = 1,

        /// <summary>
        /// The shot ignores line of sight AND cover from Sight Blockers (Indirect on a Hold): Blocking pieces are
        /// see-through for the cover test, so only Cover terrain counts - including Cover terrain behind a wall.
        /// </summary>
        Ignored = 2,
    }
}
