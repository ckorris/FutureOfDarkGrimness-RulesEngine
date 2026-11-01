namespace FDG.Tests
{
    // Returns a single fixed die value for every Roll call. Public: reused as the app-side
    // book-spell-probe's dice roller too (FdgRaylib.Tests/BookSpellCastProbeTests.cs).
    public class FixedDiceRoller : IDiceRoller
    {
        private readonly int _value;
        public FixedDiceRoller(int value) => _value = value;
        public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_value);
    }

    // Like FixedDiceRoller, but every die lands on the same fixed face AND it honors rollCount, so
    // Roll(n) reports TotalRolls == n. Needed for multi-hit/multi-save paths that FixedDiceRoller (whose
    // TotalRolls is always 1) collapses to a single roll — e.g. a Blast-multiplied spell's save rolls.
    internal sealed class FixedFaceDiceRoller : IDiceRoller
    {
        private readonly int _face;
        public FixedFaceDiceRoller(int face) => _face = face;

        public IDiceResults Roll(int sideCount, float rollCount)
        {
            float[] perSide = new float[sideCount];
            perSide[_face - 1] = rollCount;
            return new DiceResults(perSide, 1);
        }
    }

    // Exactly `ones` of the rolled dice land on a 1 and every other die on a 2 (#420): the dangerous-terrain
    // pool is one die per wound the unit would need to be destroyed, so a test that wants "one wound" can no
    // longer say it with an all-1s roller - that is now one wound per die. Honors rollCount like
    // FixedFaceDiceRoller, and never reports more 1s than dice were rolled.
    internal sealed class FixedOnesDiceRoller : IDiceRoller
    {
        private readonly int _ones;
        public FixedOnesDiceRoller(int ones) => _ones = ones;

        public IDiceResults Roll(int sideCount, float rollCount)
        {
            float[] perSide = new float[sideCount];
            float ones = Math.Min(_ones, rollCount);
            perSide[0] = ones;
            perSide[1] = rollCount - ones;
            return new DiceResults(perSide, 1);
        }
    }

    internal class FixedDiceResults : IDiceResults
    {
        private readonly int _value;
        public FixedDiceResults(int value) => _value = value;

        public float this[int index] => _value;
        public int SideMin => 1;
        public int SideMax => 6;
        public float TotalRolls => 1f;
        public float At(int rollNumber)         => rollNumber == _value ? 1f : 0f;
        public float AtOrAbove(int rollNumber)  => _value >= rollNumber ? 1f : 0f;
        public float Above(int rollNumber)      => _value >  rollNumber ? 1f : 0f;
        public float BelowOrAt(int rollNumber)  => _value <= rollNumber ? 1f : 0f;
        public float Below(int rollNumber)      => _value <  rollNumber ? 1f : 0f;
        public float Range(int lo, int hi)      => _value >= lo && _value <= hi ? 1f : 0f;
        public IDiceResults SubsetAt(int n)        => new FixedDiceResults(At(n) > 0 ? _value : 0);
        public IDiceResults SubsetAtOrAbove(int n) => new FixedDiceResults(AtOrAbove(n) > 0 ? _value : 0);
        public IDiceResults SubsetAbove(int n)     => new FixedDiceResults(Above(n) > 0 ? _value : 0);
        public IDiceResults SubsetBelowOrAt(int n) => new FixedDiceResults(BelowOrAt(n) > 0 ? _value : 0);
        public IDiceResults SubsetBelow(int n)     => new FixedDiceResults(Below(n) > 0 ? _value : 0);
        public IDiceResults SubsetRange(int lo, int hi) => new FixedDiceResults(Range(lo, hi) > 0 ? _value : 0);
    }
}
