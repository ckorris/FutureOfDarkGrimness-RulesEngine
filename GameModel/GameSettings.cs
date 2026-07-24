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

        public int TerrainPieceCount;

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

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                CoverProximityExceptions = true,
                TerrainPieceCount = 20,
                RandomnessType = ERandomnessType.Realistic,
                DiceSeed = null,
                TurnStyle = ETurnStyle.Standard,
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

    public enum ETerrainPlacementMode
    {
        /// <summary>Server places the built-in default terrain pool verbatim. Default for headless/automation.</summary>
        AutoFromLayout,

        /// <summary>Roll-off + alternating placement, one piece at a time, until <see cref="GameSettings.TerrainPieceCount"/> reached.</summary>
        Alternating,

        /// <summary>Server places the contents of <see cref="GameSettings.TerrainLayoutPath"/> verbatim.</summary>
        LoadFromFile,
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
