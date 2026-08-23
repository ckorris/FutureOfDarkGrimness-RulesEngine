using Newtonsoft.Json;

namespace FDG
{
    public struct GameSettings
    {
        public int ArmyPoints;

        /// <summary>
        /// #201 house-rule cover proximity exceptions (lobby toggle, default ON): a cover piece is
        /// voided when the shooter's muzzle hugs it (sight-line exit within 2" of the shooter's base
        /// and not also hugged by the target), or when shooter and target share the same piece at
        /// under 6" - see <see cref="FDG.Stages.CoverProximityRules"/>. Nullable so pre-#201 saves
        /// (field absent from the JSON) resolve to the default ON rather than silently OFF; read via
        /// <see cref="CoverProximityExceptionsEnabled"/>, never directly.
        /// </summary>
        public bool? CoverProximityExceptions;

        [JsonIgnore]
        public bool CoverProximityExceptionsEnabled => CoverProximityExceptions ?? true;

        /// <summary>
        /// #384 house rule (lobby toggle, default OFF): when true, NO same-team model ever blocks
        /// shooting line of sight - the pre-#384 behavior (#044). When false (official rules), only
        /// the shooting unit's own models and the target unit's models are transparent; every other
        /// unit's models, friendly or enemy, block. Plain bool, so a pre-#384 save (field absent
        /// from the JSON) resumes under the official rules - deliberate: "default = official" was
        /// the explicit ruling, even though the save was played see-through (see WorkItems/384).
        /// </summary>
        public bool SeeThroughFriendlyUnits;

        /// <summary>
        /// #384 house rule (lobby toggle, default OFF): when true, a shooting unit may split its
        /// fire across any number of distinct enemy units in one shoot action. When false, the
        /// <see cref="Utilities.GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION"/> cap (2)
        /// applies - today's behavior, so a pre-#384 save deserializes to what it was played with.
        /// </summary>
        public bool UnlimitedSplitFire;

        public int TerrainPieceCount;

        /// <summary>
        /// #301 Alternating: Points - the shared budget of terrain points the players place between
        /// them. Dealt out at phase start in placing order, <see cref="TerrainPointsPerTurn"/> at a
        /// time, until it runs out (so the last chunk may be partial and early players may get one
        /// more chunk than late ones). Read only in
        /// <see cref="ETerrainPlacementMode.AlternatingPoints"/> mode.
        /// </summary>
        public int TerrainPointsTotal;

        /// <summary>
        /// #301 Alternating: Points - how many terrain points a player spends on each of their
        /// placement turns. A piece costing more can still open a turn; the difference is taken from
        /// the player's next turn(s) as debt (see <see cref="Stages.TerrainPointsLedger"/>).
        /// </summary>
        public int TerrainPointsPerTurn;

        public ERandomnessType RandomnessType;

        /// <summary>
        /// Optional seed for <see cref="ERandomnessType.Realistic"/> dice (#167): set, the host's
        /// roller produces the same sequence every run, making manual repro cases shareable
        /// ("seed 42, scenario X, the bug appears on the second volley"). Null (the default and the
        /// value in every pre-#167 save) keeps today's unseeded behavior. Rolls happen host-side
        /// only, so the seed never needs to reach clients.
        /// </summary>
        public int? DiceSeed;

        public ETurnStyle TurnStyle;

        /// <summary>
        /// #371 - whether a unit's shooting targets are all declared before any dice are rolled, or
        /// chosen one weapon at a time with the previous weapon's casualties already on the table. See
        /// <see cref="EShootingMode"/>. Default <see cref="EShootingMode.OneAtATime"/>, which is what
        /// the game did before the setting existed - so a pre-#371 save (field absent from the JSON)
        /// deserializes to the behaviour it was played with.
        /// </summary>
        public EShootingMode ShootingMode;

        /// <summary>
        /// How objective markers are placed during map setup. See
        /// <see cref="EObjectivePlacementMode"/>. Default <see cref="EObjectivePlacementMode.AutoPlaced"/>
        /// (the engine places all markers with no player interaction).
        /// </summary>
        public EObjectivePlacementMode ObjectivePlacementMode;

        public ETerrainPlacementMode TerrainPlacementMode;

        /// <summary>
        /// Path to a <see cref="FDG.SaveLoad.TerrainLayoutFile"/> JSON, used only
        /// when <see cref="TerrainPlacementMode"/> is
        /// <see cref="ETerrainPlacementMode.LoadFromFile"/>. Resolved on the host;
        /// the file is not transmitted to clients (the engine runs on the host
        /// and broadcasts the placed terrain through table-state events).
        /// </summary>
        public string? TerrainLayoutPath;

        /// <summary>
        /// Cosmetic table surface the front end paints under the terrain (#265). Purely visual - no
        /// rule reads it - but it is a lobby setting rather than a local preference so every player at
        /// the table sees the same board, and it rides the save so a resumed game looks like the one
        /// that was put down. Default (and the value a pre-#265 save deserializes to) is
        /// <see cref="ETableBackground.Forest"/>, which is the board every existing game was played on.
        /// </summary>
        public ETableBackground TableBackground;

        /// <summary>
        /// The saved settings a resumed game launches with, with the handful of fields a resume lobby
        /// is allowed to re-pick taken from <paramref name="lobbySettings"/> (#265).
        ///
        /// <para>Today that is <see cref="TableBackground"/> and nothing else. Everything else is
        /// either already spent (army points, terrain and objective placement all happened during the
        /// saved game's setup) or would change the rules of a game in progress (randomness, dice seed,
        /// turn style, shooting mode, the cover house rules) - so the save stays authoritative for them,
        /// whatever the lobby panel happens to be showing. Adding a field here is a deliberate decision, not a
        /// default: it must be safe to change mid-game.</para>
        /// </summary>
        public GameSettings WithResumeOverridesFrom(GameSettings lobbySettings)
        {
            GameSettings merged = this;
            merged.TableBackground = lobbySettings.TableBackground;
            return merged;
        }

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                CoverProximityExceptions = true,
                SeeThroughFriendlyUnits = false,
                UnlimitedSplitFire = false,
                TerrainPieceCount = 20,
                TerrainPointsTotal = 30,
                TerrainPointsPerTurn = 3,
                RandomnessType = ERandomnessType.Realistic,
                DiceSeed = null,
                TurnStyle = ETurnStyle.Standard,
                ShootingMode = EShootingMode.OneAtATime,
                ObjectivePlacementMode = EObjectivePlacementMode.AutoPlaced,
                TerrainPlacementMode = ETerrainPlacementMode.AutoFromLayout,
                TerrainLayoutPath = null,
                TableBackground = ETableBackground.Forest,
            };
        }
    }

    /// <summary>
    /// The table's cosmetic surface (#265). Front-end only: the renderer maps each value to a felt
    /// colour, grid tint, edge trim, and mottling pattern. Forest is first so it is both the default
    /// and what <c>default(GameSettings)</c> / a pre-#265 save resolves to.
    /// </summary>
    public enum ETableBackground
    {
        Forest,
        Desert,
        Ice,
        MarsLike,
        Urban,
        Barren,
    }

    public enum ERandomnessType
    {
        Realistic,
        Probabilistic
    }

    public enum ETurnStyle
    {
        Standard,
        BoltAction
    }

    /// <summary>
    /// #371 - when a shooting unit commits to its targets.
    /// </summary>
    public enum EShootingMode
    {
        /// <summary>
        /// Pick a weapon and a target, resolve it to wounds, then pick the next weapon - already knowing
        /// what the last one killed. Declared first so it is both the default and what
        /// <c>default(GameSettings)</c> / a pre-#371 save resolves to.
        /// </summary>
        OneAtATime,

        /// <summary>
        /// Declare a target for every weapon the unit intends to fire, THEN roll them all. Casualties
        /// from an earlier weapon cannot re-aim a later one; if a declared target is wiped out before
        /// its weapon fires, those shots are lost.
        /// </summary>
        DeclareFirst,
    }

    public enum ETerrainPlacementMode
    {
        /// <summary>Server places the built-in default terrain pool verbatim. Default for headless/automation.</summary>
        AutoFromLayout,

        /// <summary>Roll-off + alternating placement, one piece at a time, until <see cref="GameSettings.TerrainPieceCount"/> reached.</summary>
        Alternating,

        /// <summary>Server places the contents of <see cref="GameSettings.TerrainLayoutPath"/> verbatim.</summary>
        LoadFromFile,

        /// <summary>
        /// #301 "Alternating: Points" - roll-off + alternating placement like <see cref="Alternating"/>
        /// ("Alternating: One Per" in the UI), but each piece costs its <see cref="SaveLoad.TerrainPieceEntry.Points"/>
        /// and a turn spends <see cref="GameSettings.TerrainPointsPerTurn"/> points (one big piece or
        /// several small ones) until <see cref="GameSettings.TerrainPointsTotal"/> is exhausted.
        /// Declared last so the wire/save value of the older modes is unchanged.
        /// </summary>
        AlternatingPoints,
    }

    public enum EObjectivePlacementMode
    {
        /// <summary>
        /// Server auto-places every objective marker with no player interaction, using the
        /// balanced placement in <see cref="FDG.Stages.ObjectiveAutoPlacer"/> (the same
        /// algorithm the solo-rules AI uses). Default; the fast path for debug/automation.
        /// </summary>
        AutoPlaced,

        /// <summary>
        /// Roll-off + alternating placement: each player is asked to place a marker in turn
        /// (human via a resolver, AI via <see cref="FDG.Ai.Resolvers.AiPlaceObjectiveResolver"/>).
        /// </summary>
        PlayerPlaced,
    }
}
