namespace FDG.Tests
{
    // Returns a single fixed die value for every Roll call.
    internal class FixedDiceRoller : IDiceRoller
    {
        private readonly int _value;
        public FixedDiceRoller(int value) => _value = value;
        public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_value);
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
