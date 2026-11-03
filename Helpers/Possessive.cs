using System;

namespace FDG
{
    /// <summary>
    /// The log line's possessive of a unit, model or weapon name: "Warriors'" / "Iron Tusk-Beast Riders'" for a name
    /// ending in s, "Ogre's" otherwise - one spelling for every narrated line (the rule evaluator, the Impact and
    /// Parry stages), so a plural unit name never reads "Warriors's".
    /// </summary>
    public static class Possessive
    {
        public static string Of(string name) =>
            name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name + "'" : name + "'s";
    }
}
