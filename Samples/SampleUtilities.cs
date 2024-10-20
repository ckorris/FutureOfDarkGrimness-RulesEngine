

namespace FDG.Samples
{
    public static class SampleUtilities
    {
        public static IDiceRoller GetDiceRoller(ERandomnessType randomnessType)
        {
            switch (randomnessType)
            {
                case ERandomnessType.Realistic:
                    return new RealisticDiceRoller();
                case ERandomnessType.Probabilistic:
                    return new ProbabilisticDiceRoller();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
