namespace FDG.SaveLoad
{
    /// <summary>
    /// Parses a special-rule reference written as a flat string — "Bane" or "Blast(3)" — into a
    /// <see cref="SpecialRuleEntry"/>. Used where rules are named as strings rather than structured
    /// entries, notably a spell's <c>DealHits.WithRules</c> (#033): "Name" →
    /// <see cref="SpecialRuleEntry_Core"/>, "Name(N)" → <see cref="SpecialRuleEntry_CoreNumeric"/>.
    /// A malformed numeric falls back to a plain core name (the resolver then skips it with a warning).
    /// </summary>
    public static class SpecialRuleEntryParser
    {
        public static SpecialRuleEntry Parse(string raw)
        {
            string text = raw.Trim();

            int open = text.IndexOf('(');
            if (open < 0)
            {
                return new SpecialRuleEntry_Core(text);
            }

            int close = text.IndexOf(')', open);
            if (close > open)
            {
                string name = text.Substring(0, open).Trim();
                string inner = text.Substring(open + 1, close - open - 1).Trim();
                if (int.TryParse(inner, out int value))
                {
                    return new SpecialRuleEntry_CoreNumeric(name, value);
                }
            }

            return new SpecialRuleEntry_Core(text);
        }
    }
}
