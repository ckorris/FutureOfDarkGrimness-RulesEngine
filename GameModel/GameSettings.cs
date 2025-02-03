
namespace FDG
{
    public struct GameSettings
    {
        public int ArmyPoints;

        public int TerrainPieceCount;

        public ERandomnessType RandomnessType;

        public ETurnStyle TurnStyle;

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                TerrainPieceCount = 12,
                RandomnessType = ERandomnessType.Realistic,
                TurnStyle = ETurnStyle.Standard
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
