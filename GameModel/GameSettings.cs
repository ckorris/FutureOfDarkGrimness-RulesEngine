
namespace FDG
{
    public struct GameSettings
    {
        public int ArmyPoints;

        public int TerrainPieceCount;

        public ERandomnessType RandomnessType;

        public ETurnStyle TurnStyle;

        /// <summary>
        /// Debug: when true, <see cref="FDG.Stages.PlaceObjectivesStage"/> skips
        /// interactive placement and runs the legacy grid-RNG auto-placer. Off by
        /// default; intended for headless / piped automation only.
        /// </summary>
        public bool AutoPlaceObjectivesDebug;

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
                TurnStyle = ETurnStyle.Standard,
                AutoPlaceObjectivesDebug = false,
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
}
