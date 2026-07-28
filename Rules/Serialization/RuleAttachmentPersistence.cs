using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;

namespace FDG.Rules.Serialization
{
    /// <summary>
    /// #095: persists the special rules attached to a carrier (unit / model / weapon) as a
    /// System.Text.Json blob so they survive a save/load resume.
    /// <para>
    /// <c>RuleDefinitions</c> is <c>[JsonIgnore]</c> on every carrier: the resolved
    /// <see cref="SpecialRuleDefinition"/> graph cannot round-trip through the Newtonsoft save/store
    /// layer (Newtonsoft chokes on the rule tree's computed members — the same reason
    /// <c>ArmyListUpdateMessage</c> ships armies as an STJ blob over the wire). Each carrier therefore
    /// persists this opaque STJ string and rehydrates from it on load. The blob carries the full resolved
    /// definition (core, custom, alias, override) plus each attachment's arguments, so rehydration needs
    /// no rule resolver and no army file — both of which are unavailable on the resume path.
    /// </para>
    /// <para>
    /// This restores rule <i>objects</i> only. Creation-time effects (e.g. Tough's max-wounds) are NOT
    /// re-applied here; those stay gated to fresh-game creation in <c>FDGServer</c>, so a resume neither
    /// loses the rule nor double-applies its creation effect.
    /// </para>
    /// </summary>
    public static class RuleAttachmentPersistence
    {
        // Mirrors ResolvedRule. Arguments used to be flattened to ints (RuleArgument.Int was the only
        // variant); #197 P17 added Str, so `Arguments` now carries every kind as a one-of record.
        // `IntArguments` stays so a pre-P17 blob still deserializes — a NEW blob writes both, with
        // `Arguments` authoritative on read.
        public sealed record PersistedArgument(int? IntValue = null, string? StrValue = null);

        public sealed record PersistedResolvedRule(string RequestedName, SpecialRuleDefinition Definition,
            List<int> IntArguments, List<PersistedArgument>? Arguments = null);

        public static string Serialize(IReadOnlyList<ResolvedRule> rules)
        {
            List<PersistedResolvedRule> persisted = new List<PersistedResolvedRule>(rules.Count);

            foreach (ResolvedRule rule in rules)
            {
                List<int> intArguments = new List<int>(rule.Arguments.Count);
                List<PersistedArgument> arguments = new List<PersistedArgument>(rule.Arguments.Count);
                foreach (RuleArgument argument in rule.Arguments)
                {
                    switch (argument)
                    {
                        case RuleArgument.Int intArgument:
                            intArguments.Add(intArgument.Value);
                            arguments.Add(new PersistedArgument(IntValue: intArgument.Value));
                            break;
                        case RuleArgument.Str strArgument:
                            arguments.Add(new PersistedArgument(StrValue: strArgument.Value));
                            break;
                        default:
                            throw new NotSupportedException(
                                $"Rule '{rule.RequestedName}' carries an argument kind " +
                                $"({argument.GetType().Name}) this persistence does not know; " +
                                "extend PersistedArgument before shipping the new kind.");
                    }
                }

                persisted.Add(new PersistedResolvedRule(rule.RequestedName, rule.Definition, intArguments,
                    arguments));
            }

            return JsonSerializer.Serialize(persisted, RuleJson.Options);
        }

        public static List<ResolvedRule> Deserialize(string json)
        {
            List<PersistedResolvedRule>? persisted =
                JsonSerializer.Deserialize<List<PersistedResolvedRule>>(json, RuleJson.Options);

            List<ResolvedRule> rules = new List<ResolvedRule>(persisted?.Count ?? 0);
            if (persisted == null)
            {
                return rules;
            }

            foreach (PersistedResolvedRule entry in persisted)
            {
                // `Arguments` is authoritative when present; a pre-P17 blob only has the int list.
                IReadOnlyList<RuleArgument> arguments = entry.Arguments != null
                    ? entry.Arguments
                        .Select(a => a.StrValue != null
                            ? (RuleArgument)new RuleArgument.Str(a.StrValue)
                            : new RuleArgument.Int(a.IntValue ?? 0))
                        .ToList()
                    : entry.IntArguments
                        .Select(value => (RuleArgument)new RuleArgument.Int(value))
                        .ToList();
                rules.Add(new ResolvedRule(entry.RequestedName, entry.Definition, arguments));
            }

            return rules;
        }
    }
}
