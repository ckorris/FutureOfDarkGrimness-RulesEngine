using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #033 — composes a short, human-readable description of a spell (e.g. "2 hits (AP(1)) — one enemy
    /// unit within 18&quot;") for the spell-selection menu. Built engine-side so the CLI and GUI resolvers
    /// render the same subtext from the request, with no spell knowledge of their own.
    /// </summary>
    public static class SpellText
    {
        public static string Describe(SpellDefinition spell)
            => $"{DescribeEffect(spell.Effect)} — {DescribeTarget(spell.Target)}";

        private static string DescribeEffect(Effect effect)
        {
            switch (effect)
            {
                case Effect.DealHits dealHits:
                {
                    string text = $"{dealHits.Count} hit{Plural(dealHits.Count)}";
                    List<string> modifiers = new List<string>();
                    if (dealHits.ArmorPenetration > 0) modifiers.Add($"AP({dealHits.ArmorPenetration})");
                    modifiers.AddRange(dealHits.WithRules);
                    if (modifiers.Count > 0) text += $" ({string.Join(", ", modifiers)})";
                    return text;
                }
                case Effect.AddRule addRule:
                    return $"grants {addRule.RuleName} ({DescribeLifetime(addRule.Scope)})";
                default:
                    return "applies an effect";
            }
        }

        private static string DescribeTarget(TargetSelector target)
        {
            string count = target.MaxCount <= 1 ? "one" : $"up to {target.MaxCount}";
            string affinity = target.TargetAffinity switch
            {
                ETargetAffinity.Foe => "enemy",
                ETargetAffinity.Friend => "friendly",
                ETargetAffinity.Self => "self",
                _ => "any",
            };
            string text = $"{count} {affinity} unit{Plural(target.MaxCount)} within {Inches(target.RangeInches)}\"";
            if (target.RequireLineOfSight) text += ", in line of sight";
            return text;
        }

        private static string DescribeLifetime(ELifetime lifetime) => lifetime switch
        {
            ELifetime.NextTrigger => "next time",
            ELifetime.ThisActivation => "this activation",
            ELifetime.ThisRound => "this round",
            ELifetime.UntilEndOfGame => "rest of game",
            _ => lifetime.ToString(),
        };

        private static string Plural(int count) => count == 1 ? "" : "s";

        private static string Inches(float value) => value % 1f == 0f ? ((int)value).ToString() : value.ToString("0.#");
    }
}
