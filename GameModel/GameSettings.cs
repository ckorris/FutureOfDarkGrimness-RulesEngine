
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

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                TerrainPieceCount = 12,
                RandomnessType = ERandomnessType.Realistic,
                TurnStyle = ETurnStyle.Standard,
                AutoPlaceObjectivesDebug = false,
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
}
