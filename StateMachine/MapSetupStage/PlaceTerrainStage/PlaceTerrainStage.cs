using FDG.SaveLoad;

namespace FDG.Stages
{
    public class PlaceTerrainStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnTerrainPlaced;

        public PlaceTerrainStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnTerrainPlaced = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.Log($"Entered {nameof(PlaceTerrainStage)}.");

            foreach (var entry in BuildTestLayout().Pieces)
            {
                if (entry.Shape == null) continue;
                context.GameContext.GameDataStore.Create(new TerrainData(entry.TerrainType, entry.Shape, entry.HeightInches));
            }

            OnTerrainPlaced.Activate(context);
        }

        // Hardcoded layout for now — interactive placement comes later.
        private static TerrainLayoutFile BuildTestLayout() => new TerrainLayoutFile
        {
            Name = "Test Layout",
            Pieces = new List<TerrainPieceEntry>
            {
                // Center building — blocking + impassible.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(33, 39, 22, 26),
                    HeightInches = 4f,
                },
                // Forest, left-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(20, 24, 5),
                    HeightInches = 0f,
                },
                // Forest, right-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(52, 24, 5),
                    HeightInches = 0f,
                },
                // Sandbags near each deployment line — cover.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    HeightInches = 0f,
                },
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(36, 44, 35, 36),
                    HeightInches = 0f,
                },
                // Mine field — dangerous.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(8, 14, 30, 36),
                    HeightInches = 0f,
                },
                // Rubble — difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(58, 66, 12, 18),
                    HeightInches = 0f,
                },
            }
        };
    }
}
