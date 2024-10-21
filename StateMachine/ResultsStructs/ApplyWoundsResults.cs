
namespace FDG
{
    public readonly struct ApplyWoundsResults
    {
        public readonly int ModelsKilled;
        public ApplyWoundsResults(int modelsKilled)
        {
            ModelsKilled = modelsKilled;
        }
    }
}