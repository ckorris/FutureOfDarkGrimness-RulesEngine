
namespace FDG
{
    public struct OcclusionCheckResults
    {
        public bool IsOccluded { get; }
        public OcclusionCheckResults(bool isOccluded) { IsOccluded = isOccluded; }
    }
}