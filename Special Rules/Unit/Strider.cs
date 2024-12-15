
using FDG.Stages;

namespace FDG
{
    public class Strider : SpecialRule_Movement
    {
        public override void ProcessMovementContextPrecursor(ref MovementContextPrecursor precursor)
        {
            precursor.RelevantTerrain.RemoveAll(terrain => terrain.TerrainType == ETerrainType.Difficult);
        }
    }
}