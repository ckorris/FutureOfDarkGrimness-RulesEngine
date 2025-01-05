

public class ProbabilisticDiceRoller : IDiceRoller
{
    public IDiceResults Roll(int sideCount, float rollCount)
    {
        float[] rolls = new float[sideCount];
        float valueOfAllSides = rollCount / sideCount;

        for(int i = 0; i < rolls.Length; i++)
        {
            rolls[i] = valueOfAllSides;
        }

        return new DiceResults(rolls);
    }
}
