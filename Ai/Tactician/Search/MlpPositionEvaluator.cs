using System.Text.Json;
using FDG.Ai.Tactician.Learning;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;

namespace FDG.Ai.Tactician.Search
{
    /// <summary>
    /// A learned leaf evaluator (#191 phase C, campaign step 14): the schema-v3 serving vector
    /// (<see cref="PositionEncoder.EncodeForEvaluation"/>) through a small dense net, one forward
    /// pass per side.
    /// <para>
    /// The forward pass is written out here rather than run through ONNX Runtime, and that is a
    /// deliberate call recorded in the plan (sec 10, step 14): at this size - three layers, about
    /// 20k weights - a dense pass is tens of microseconds, while a native runtime would add a
    /// platform-specific dependency to four unsigned release archives for no measurable gain. The
    /// python side exports an ONNX file alongside the weights purely as a parity oracle, so a test
    /// can prove this implementation agrees with what was trained.
    /// </para>
    /// <para>
    /// Contract, per <see cref="IPositionEvaluator"/>: a pure read, no dice, no mutation. Two-side
    /// games are symmetrized so <see cref="SideValues.IsComplementaryTwoSide"/> holds by
    /// construction - the net is trained per side and its two answers need not sum to one, and a
    /// search that backs up non-complementary values in a zero-sum game is reading noise as signal.
    /// </para>
    /// </summary>
    public sealed class MlpPositionEvaluator : IPositionEvaluator
    {
        private readonly Layer[] _layers;
        private readonly int _inputWidth;

        private readonly struct Layer
        {
            public readonly float[][] Weight; // [out][in]
            public readonly float[] Bias;
            public readonly bool Relu;

            public Layer(float[][] weight, float[] bias, bool relu)
            {
                Weight = weight;
                Bias = bias;
                Relu = relu;
            }
        }

        private MlpPositionEvaluator(Layer[] layers, int inputWidth)
        {
            _layers = layers;
            _inputWidth = inputWidth;
        }

        /// <summary>Loads the weights JSON written by <c>FdgLab/python/train.py --export</c>.</summary>
        public static MlpPositionEvaluator FromJson(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            int schema = root.TryGetProperty("schema", out JsonElement s) ? s.GetInt32() : 0;
            if (schema != PositionEncoder.SchemaVersion)
                throw new InvalidOperationException(
                    $"MlpPositionEvaluator: weights were trained on feature schema v{schema}, "
                    + $"this build encodes v{PositionEncoder.SchemaVersion}. Retrain or check out the matching build.");

            int featureCount = root.GetProperty("features").GetArrayLength();
            if (featureCount != PositionEncoder.ServingVectorWidth)
                throw new InvalidOperationException(
                    $"MlpPositionEvaluator: weights expect {featureCount} inputs, the serving encoder "
                    + $"produces {PositionEncoder.ServingVectorWidth}.");

            var layers = new List<Layer>();
            foreach (JsonElement layer in root.GetProperty("layers").EnumerateArray())
            {
                JsonElement weightElement = layer.GetProperty("weight");
                var weight = new float[weightElement.GetArrayLength()][];
                int row = 0;
                foreach (JsonElement weightRow in weightElement.EnumerateArray())
                {
                    var values = new float[weightRow.GetArrayLength()];
                    int column = 0;
                    foreach (JsonElement value in weightRow.EnumerateArray()) values[column++] = value.GetSingle();
                    weight[row++] = values;
                }

                JsonElement biasElement = layer.GetProperty("bias");
                var bias = new float[biasElement.GetArrayLength()];
                int index = 0;
                foreach (JsonElement value in biasElement.EnumerateArray()) bias[index++] = value.GetSingle();

                bool relu = layer.GetProperty("activation").GetString() == "relu";
                if (weight.Length != bias.Length)
                    throw new InvalidOperationException("MlpPositionEvaluator: layer weight/bias length mismatch.");
                layers.Add(new Layer(weight, bias, relu));
            }

            if (layers.Count == 0) throw new InvalidOperationException("MlpPositionEvaluator: no layers in the weights file.");
            if (layers[^1].Bias.Length < 1)
                throw new InvalidOperationException("MlpPositionEvaluator: the output layer has no units.");
            return new MlpPositionEvaluator(layers.ToArray(), featureCount);
        }

        public static MlpPositionEvaluator FromFile(string path) => FromJson(File.ReadAllText(path));

        /// <summary>
        /// The net's value for one feature vector: sigmoid of output unit 0, the head trained on
        /// the game result (1 win / 0.5 tie / 0 loss) for the side the vector describes.
        /// </summary>
        public float Value(float[] features)
        {
            if (features.Length != _inputWidth)
                throw new ArgumentException($"expected {_inputWidth} features, got {features.Length}", nameof(features));

            float[] activations = features;
            foreach (Layer layer in _layers)
            {
                var next = new float[layer.Bias.Length];
                for (int unit = 0; unit < next.Length; unit++)
                {
                    float[] weights = layer.Weight[unit];
                    float sum = layer.Bias[unit];
                    for (int i = 0; i < weights.Length; i++) sum += weights[i] * activations[i];
                    next[unit] = layer.Relu && sum < 0f ? 0f : sum;
                }
                activations = next;
            }
            return 1f / (1f + MathF.Exp(-activations[0]));
        }

        public SideValues Evaluate(ITableState state, RuleEvaluator evaluator, SideMap sides)
        {
            var membersBySide = new List<PlayerID>[sides.Count];
            for (int side = 0; side < sides.Count; side++) membersBySide[side] = new List<PlayerID>();
            foreach (PlayerID player in sides.Players.OrderBy(p => p.ToString(), StringComparer.Ordinal))
                membersBySide[sides.SideOf(player)].Add(player);

            var raw = new float[sides.Count];
            for (int side = 0; side < sides.Count; side++)
            {
                if (membersBySide[side].Count == 0)
                {
                    raw[side] = 0.5f;
                    continue;
                }
                var opposing = new List<PlayerID>();
                for (int other = 0; other < sides.Count; other++)
                    if (other != side) opposing.AddRange(membersBySide[other]);
                raw[side] = Value(PositionEncoder.EncodeForEvaluation(state, evaluator, membersBySide[side], opposing));
            }

            var values = new SideValues(sides.Count);
            if (sides.Count == 2)
            {
                // Two sides, zero sum: average the two independent reads into one complementary pair.
                float value = (raw[0] + 1f - raw[1]) / 2f;
                values[0] = Math.Clamp(value, 0f, 1f);
                values[1] = Math.Clamp(1f - value, 0f, 1f);
                return values;
            }

            float total = raw.Sum();
            for (int side = 0; side < sides.Count; side++)
                values[side] = total <= 0f ? 1f / sides.Count : Math.Clamp(raw[side] / total, 0f, 1f);
            return values;
        }
    }
}
