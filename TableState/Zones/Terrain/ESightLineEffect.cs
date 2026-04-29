
namespace FDG
{
    /// <summary>
    /// What a single terrain piece does to a sight line drawn between two points.
    /// Aggregated across multiple pieces by priority: Blocking > Cover > Clear.
    /// </summary>
    public enum ESightLineEffect
    {
        Clear = 0,
        Cover = 1,
        Blocking = 2,
    }
}
