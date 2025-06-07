using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FDG.SaveLoad
{
    public interface ISpecialRuleEntry
    {
        string PrintableName { get; }
    }

    /// <summary>
    /// Holds a "core" rule, that is, one that appears in the base rules, like "Regeneration".
    /// This does not include ones that have a variable in it.
    /// </summary>
    public readonly struct SpecialRuleEntry_Core : ISpecialRuleEntry
    {
        public string PrintableName => Name;

        public string Name { get; }

    }

    /// <summary>
    /// Holds a "core" rule that appears in the base rules and that also has a numeric value
    /// as part of it, like "Tough(X)".
    /// </summary>
    public readonly struct SpecialRuleEntry_Core_Numeric : ISpecialRuleEntry
    {
        public string PrintableName => $"{Name}({NumericValue})";

        public string Name { get; }

        public int NumericValue { get; }
    }

    /// <summary>
    /// Holds a rule that is just a renaming of a different rule and that works the same way.
    /// An example would be Battle Brothers' "Medical Training (Regeneration)".
    /// </summary>
    public readonly struct SpecialRuleEntry_Alias : ISpecialRuleEntry
    {
        public string PrintableName => $"{Name} ({AliasedRule.PrintableName})";

        public string Name { get; }

        public ISpecialRuleEntry AliasedRule { get; }
    }
}