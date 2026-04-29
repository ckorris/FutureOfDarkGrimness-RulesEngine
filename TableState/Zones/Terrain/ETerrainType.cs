
namespace FDG
{
    [Flags]
    public enum ETerrainType
    {
        None       = 0,
        Cover      = 1 << 0,
        Impassible = 1 << 1,
        Difficult  = 1 << 2,
        Dangerous  = 1 << 3,
        Blocking   = 1 << 4,
        Elevated   = 1 << 5,
    }
}
