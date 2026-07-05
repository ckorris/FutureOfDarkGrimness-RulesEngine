using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A dice roll, surfaced so the front-end can show it (tumbling dice in the forefront, a
    /// probability bar, etc.). Carries the per-face counts as plain floats rather than an
    /// <c>IDiceResults</c>, both to stay a clean serializable DTO and because the engine's two
    /// roller modes produce genuinely different shapes that the front-end must render differently:
    /// <list type="bullet">
    /// <item><b>Realistic</b> — whole-number counts; a concrete multiset of faces to draw as dice.</item>
    /// <item><b>Probabilistic</b> — <c>rollCount/sideCount</c> per face; fractional, no discrete dice
    /// exist, so the front-end shows a fractional / expected-value vocabulary instead.</item>
    /// </list>
    /// The mode rides on the beat (rather than being inferred from integrality) because probabilistic
    /// counts can land on whole numbers by coincidence.
    /// </summary>
    [Serializable]
    public sealed class DiceRolledBeat : PresentationBeat
    {
        /// <summary>Count showing each face: <c>FaceCounts[i]</c> is the count on face <c>SideMin + i</c>.</summary>
        public IReadOnlyList<float> FaceCounts { get; }

        public int SideMin { get; }

        /// <summary>Faces at or above this count as successes.</summary>
        public int SuccessThreshold { get; }

        public ERandomnessType Mode { get; }

        /// <summary>Short context for display, e.g. "Roll to Hit", "Roll to Save" — the "what for"
        /// shown while the dice tumble.</summary>
        public string Label { get; }

        /// <summary>
        /// Optional plain-language outcome shown once the dice settle — the "what it means", e.g.
        /// "2 saved, 3 wounds" or "Passed". Null falls back to a generic "{successes} / {total}".
        /// Set by the rolling stage, which alone knows the roll's semantics.
        /// </summary>
        public string? ResultSummary { get; }

        public DiceRolledBeat(IReadOnlyList<float> faceCounts, int sideMin, int successThreshold,
            ERandomnessType mode, string label, string? resultSummary = null, bool held = false)
        {
            FaceCounts = faceCounts;
            SideMin = sideMin;
            SuccessThreshold = successThreshold;
            Mode = mode;
            Label = label;
            ResultSummary = resultSummary;
            Held = held;
        }

        /// <summary>
        /// When true, the settled dice linger on screen after their lead-in (via the held-beat
        /// mechanism) so the result stays visible while the wounds it produced animate. Serializes so a
        /// networked client holds the dice too.
        /// </summary>
        public override bool Held { get; }

        /// <summary>Long enough for the faces to flick and settle before the engine moves on.</summary>
        public override TimeSpan HoldLeadIn => TimeSpan.FromMilliseconds(600);

        public int SideMax => SideMin + FaceCounts.Count - 1;

        public float Total
        {
            get { float t = 0f; foreach (float c in FaceCounts) t += c; return t; }
        }

        public float Successes
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < FaceCounts.Count; i++)
                    if (SideMin + i >= SuccessThreshold) s += FaceCounts[i];
                return s;
            }
        }

        public override TimeSpan NominalDuration => PresentationDurations.DiceRoll;

        public override string? Text => $"{Label} ({SuccessThreshold}+): {Successes:0.##} / {Total:0.##}";

        /// <summary>
        /// Build a beat from an engine roll result, capturing the per-face histogram.
        /// </summary>
        public static DiceRolledBeat From(IDiceResults results, int successThreshold, ERandomnessType mode,
            string label, string? resultSummary = null, bool held = false)
        {
            int min = results.SideMin, max = results.SideMax;
            float[] faceCounts = new float[max - min + 1];
            for (int f = min; f <= max; f++)
                faceCounts[f - min] = results.At(f);

            return new DiceRolledBeat(faceCounts, min, successThreshold, mode, label, resultSummary, held);
        }
    }
}
