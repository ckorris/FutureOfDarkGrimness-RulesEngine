using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// What a <see cref="DiceRolledBeat"/> is rolled FOR, at the coarse level the front-end
    /// color-codes (#245): an offensive roll (to-hit), a defensive roll (saves, regeneration,
    /// Bane re-rolls), or anything else (morale, objectives, dangerous terrain). Serializes so a
    /// networked client colors the panel the same way.
    /// </summary>
    public enum ERollBeatCategory
    {
        Misc = 0,
        Offense = 1,
        Defense = 2,
    }

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

        /// <summary>
        /// The SHAPE of <see cref="FaceCounts"/>, not the game's randomness setting — which vocabulary
        /// the front-end must use to draw this particular roll. Usually they agree, but a DECISIVE roll
        /// (<see cref="IDiceRoller.RollDecisive"/>) commits to concrete faces even under the probabilistic
        /// roller, so it is <see cref="ERandomnessType.Realistic"/> regardless of the setting — see
        /// <see cref="FromDecisive"/> (#289).
        /// </summary>
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

        /// <summary>Coarse purpose of the roll (#245) — drives the front-end's color coding. Optional;
        /// legacy emitters default to <see cref="ERollBeatCategory.Misc"/>.</summary>
        public ERollBeatCategory Category { get; }

        /// <summary>
        /// Optional "who" line (#245), e.g. "Warriors -> Heavy Gunners" for a to-hit roll or the
        /// defender's name for a save — grounds the roll without the player scanning the table.
        /// </summary>
        public string? Context { get; }

        /// <summary>
        /// Optional display-ready modifier chips (#245) explaining how the threshold came to be, e.g.
        /// ["Quality 4+", "Stealth -1"]. Pre-composed by the emitting stage (which alone knows the
        /// arithmetic); the front-end renders them verbatim. Null/empty when the threshold is unmodified.
        /// </summary>
        public IReadOnlyList<string>? ModifierTags { get; }

        /// <summary>
        /// Optional display-ready proc chips (#245) for face-triggered rule effects that fired on this
        /// roll, e.g. ["Furious +2 on 6s"]. The front-end highlights the top-face successes when present.
        /// </summary>
        public IReadOnlyList<string>? ProcTags { get; }

        public DiceRolledBeat(IReadOnlyList<float> faceCounts, int sideMin, int successThreshold,
            ERandomnessType mode, string label, string? resultSummary = null, bool held = false,
            ERollBeatCategory category = ERollBeatCategory.Misc, string? context = null,
            IReadOnlyList<string>? modifierTags = null, IReadOnlyList<string>? procTags = null)
        {
            FaceCounts = faceCounts;
            SideMin = sideMin;
            SuccessThreshold = successThreshold;
            Mode = mode;
            Label = label;
            ResultSummary = resultSummary;
            Held = held;
            Category = category;
            Context = context;
            ModifierTags = modifierTags;
            ProcTags = procTags;
        }

        /// <summary>
        /// When true, the settled dice linger on screen after their lead-in (via the held-beat
        /// mechanism) so the result stays visible while the wounds it produced animate. Serializes so a
        /// networked client holds the dice too.
        /// </summary>
        public override bool Held { get; }

        /// <summary>Long enough for the faces to flick and settle before the engine moves on. Extended
        /// when there are info chips to read (#245), like the full duration.</summary>
        public override TimeSpan HoldLeadIn => TimeSpan.FromMilliseconds(600 + 200 * InfoBlocks);

        /// <summary>
        /// How many optional info blocks (modifier chips, proc chips) this beat carries — each one is
        /// extra reading, so each stretches the beat: +400ms on the full duration, +200ms on a held
        /// beat's lead-in. The context line is a glance and adds nothing.
        /// </summary>
        public int InfoBlocks =>
            (ModifierTags is { Count: > 0 } ? 1 : 0) + (ProcTags is { Count: > 0 } ? 1 : 0);

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

        public override TimeSpan NominalDuration =>
            PresentationDurations.DiceRoll + TimeSpan.FromMilliseconds(400 * InfoBlocks);

        public override string? Text => $"{Label} ({SuccessThreshold}+): {Successes:0.##} / {Total:0.##}";

        /// <summary>
        /// Build a beat from an engine roll result, capturing the per-face histogram.
        /// </summary>
        public static DiceRolledBeat From(IDiceResults results, int successThreshold, ERandomnessType mode,
            string label, string? resultSummary = null, bool held = false,
            ERollBeatCategory category = ERollBeatCategory.Misc, string? context = null,
            IReadOnlyList<string>? modifierTags = null, IReadOnlyList<string>? procTags = null)
        {
            int min = results.SideMin, max = results.SideMax;
            float[] faceCounts = new float[max - min + 1];
            for (int f = min; f <= max; f++)
                faceCounts[f - min] = results.At(f);

            return new DiceRolledBeat(faceCounts, min, successThreshold, mode, label, resultSummary, held,
                category, context, modifierTags, procTags);
        }

        /// <summary>
        /// #289 — a beat for a roll made with <see cref="IDiceRoller.RollDecisive"/>. Such a roll yields
        /// whole, concrete faces in BOTH roller modes (that is the whole point of the decisive path: a
        /// morale pass/fail or an objective count cannot be averaged), so the beat always declares
        /// <see cref="ERandomnessType.Realistic"/> and the front-end draws real dice. Emitting these with
        /// the game's <c>Settings.RandomnessType</c> instead made a probabilistic game show an
        /// expected-value bar for a die that had genuinely been rolled.
        ///
        /// <para>Deliberately not inferred front-end side from "the counts happen to be integral":
        /// a probabilistic pool can land on whole numbers by coincidence, so only the roll site knows.</para>
        /// </summary>
        public static DiceRolledBeat FromDecisive(IDiceResults results, int successThreshold,
            string label, string? resultSummary = null, bool held = false,
            ERollBeatCategory category = ERollBeatCategory.Misc, string? context = null,
            IReadOnlyList<string>? modifierTags = null, IReadOnlyList<string>? procTags = null)
            => From(results, successThreshold, ERandomnessType.Realistic, label, resultSummary, held,
                category, context, modifierTags, procTags);
    }
}
