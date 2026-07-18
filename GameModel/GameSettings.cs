
namespace FDG
{
    public struct GameSettings
    {
        public int ArmyPoints;

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

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                TerrainPieceCount = 20,
                RandomnessType = ERandomnessType.Realistic,
                DiceSeed = null,
                TurnStyle = ETurnStyle.Standard,
                ObjectivePlacementMode = EObjectivePlacementMode.AutoPlaced,
                TerrainPlacementMode = ETerrainPlacementMode.AutoFromLayout,
                TerrainLayoutPath = null,
            };
        }
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
