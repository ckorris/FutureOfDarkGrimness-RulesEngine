namespace FDG.Tests.RulesHarness
{
    /// <summary>
    /// Builds <see cref="DiceResults"/> for rule tests from a list of individual die
    /// faces — the per-face histogram the engine actually uses, as a realistic roller
    /// would produce it. <c>TestDice.Faces(6, 3, 6)</c> yields <c>At(6) == 2</c>,
    /// <c>At(3) == 1</c>. Single d6 overload only; everything in the corpus rolls d6.
    /// </summary>
    internal static class TestDice
    {
        private const int D6SideCount = 6;

        public static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[D6SideCount];
            foreach (int face in faces)
            {
                perSide[face - 1] += 1f;
            }

            return new DiceResults(perSide);
        }
    }
}
