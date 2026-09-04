namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// One value per side, each in [0, 1] (#191 B2 sec 7.1) - the reward vector G13(c) asks for.
    /// A leaf's values are added UNCHANGED to every node on its path (sec 7.3: no discounting, no
    /// shaping in the backup - the tree has no reward knobs to hack), and a node's selection reads
    /// only its acting side's component (sec 7.4: max^n).
    /// </summary>
    public sealed class SideValues
    {
        private readonly float[] _values;

        public SideValues(int sideCount) => _values = new float[sideCount];

        public SideValues(params float[] values) => _values = (float[])values.Clone();

        public int Count => _values.Length;

        public float this[int side]
        {
            get => _values[side];
            set => _values[side] = value;
        }

        public void AddInPlace(SideValues other)
        {
            if (other.Count != Count)
                throw new ArgumentException($"SideValues: cannot add {other.Count} sides into {Count}.");
            for (int i = 0; i < _values.Length; i++) _values[i] += other._values[i];
        }

        public SideValues Clone() => new(_values);

        public static SideValues Uniform(int sideCount, float value)
        {
            var result = new SideValues(sideCount);
            for (int i = 0; i < sideCount; i++) result._values[i] = value;
            return result;
        }

        /// <summary>
        /// Terminal values (sec 7.1): 1.0 for the winning side and 0.0 for every other on a Win; 0.5
        /// for every side on a Tie. A Fault or Disconnect is not a node and must not reach here.
        /// </summary>
        public static SideValues FromResult(GameResult result, SideMap sides)
        {
            switch (result.Outcome)
            {
                case EGameOutcome.Tie:
                    return Uniform(sides.Count, 0.5f);
                case EGameOutcome.Win:
                    var values = new SideValues(sides.Count);
                    values[sides.SideOf(result.WinnerPlayers[0])] = 1f;
                    return values;
                default:
                    throw new ArgumentException(
                        $"SideValues: a {result.Outcome} game has no terminal value ({result.Message}).");
            }
        }

        /// <summary>
        /// The two-side constraint every shipped evaluator must satisfy (sec 7.2): with exactly two
        /// sides, v[other] == 1 - v[self], which is what makes max^n reduce to minimax in 1v1.
        /// </summary>
        public bool IsComplementaryTwoSide(float tolerance = 1e-5f) =>
            Count == 2 && MathF.Abs(_values[0] + _values[1] - 1f) <= tolerance;

        public override string ToString() => "[" + string.Join(", ", _values.Select(v => v.ToString("F3"))) + "]";
    }
}
