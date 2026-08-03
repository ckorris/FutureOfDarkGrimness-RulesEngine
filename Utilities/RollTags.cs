namespace FDG.Utilities
{
    /// <summary>
    /// Formatting helpers for the #245 dice-beat info chips (DiceRolledBeat.ModifierTags / ProcTags):
    /// short display-ready strings composed at the emitting stage, rendered verbatim by the front-end.
    /// ASCII-only by project convention.
    /// </summary>
    public static class RollTags
    {
        /// <summary>A signed delta as chip text: +1 / -2. Positive is always the roller's favor
        /// (the stages' shared "threshold -= delta" convention).</summary>
        public static string Delta(int delta) => delta >= 0 ? $"+{delta}" : delta.ToString();

        /// <summary>A rule name for a chip, falling back when a book aliased the name away.</summary>
        public static string NameOr(string? ruleName, string fallback) =>
            string.IsNullOrEmpty(ruleName) ? fallback : ruleName;

        /// <summary>A rolled count + noun with number agreement: "1 hit", "2 hits", "0 hits".
        /// Probabilistic counts are fractional, so the DISPLAYED 0.## rendering decides - singular
        /// only when it reads exactly "1" (a shown "1.02" stays plural, matching what the eye sees).</summary>
        public static string Count(float count, string noun)
        {
            string formatted = count.ToString("0.##");
            return formatted == "1" ? $"{formatted} {noun}" : $"{formatted} {noun}s";
        }
    }
}
