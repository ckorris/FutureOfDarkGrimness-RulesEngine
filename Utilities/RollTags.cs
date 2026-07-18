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
    }
}
