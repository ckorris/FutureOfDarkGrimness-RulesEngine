using System.Collections.Generic;
using System.Linq;
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
        {
            // ASCII-only (the default ImGui font has no em-dash) and sentence-cased so "grants ..." reads
            // as "Grants ...".
            string text = $"{DescribeEffect(spell.Effect)} - {DescribeTarget(spell.Target)}";
            return text.Length > 0 ? char.ToUpper(text[0]) + text.Substring(1) : text;
        }

        private static string DescribeEffect(Effect effect)
        {
            switch (effect)
            {
                case Effect.DealHits dealHits:
                    return $"{dealHits.Count} hit{Plural(dealHits.Count)}{DescribeHitModifiers(dealHits)}";
                case Effect.AddRule addRule:
                    return $"grants {addRule.RuleName} ({DescribeLifetime(addRule.Scope)})";
                case Effect.StatModifier statMod:
                {
                    string sign = statMod.Delta >= 0 ? "+" : "";
                    return $"{sign}{statMod.Delta} to {RollName(statMod.Roll)} ({DescribeLifetime(statMod.LifetimeScope)})";
                }
                case Effect.TriggeredMove move:
                    return $"moves the target up to {Inches(move.MaxInches)}\"";
                case Effect.MoraleTestThen conditional:
                    return $"morale test; on a fail, {DescribeEffect(conditional.OnFailure)}";
                case Effect.ApplyFatigue:
                    return "becomes fatigued";
                case Effect.MarkTarget mark:
                    return $"marks it - the next friendly to attack it gets {mark.RuleName}";
                default:
                    return "applies an effect";
            }
        }

        /// <summary>
        /// #293 — what a resolved spell DID, for the result banner. The picker's
        /// <see cref="Describe"/> is a spell's advertisement ("grants Rending (this round) - one friendly
        /// unit within 12&quot;"); this is the report ("Bless grants Rending to Knight Brothers (this
        /// round)"), naming the units it actually landed on. ASCII only (CLAUDE.md).
        /// </summary>
        public static string DescribeApplied(string spellName, Effect effect, IReadOnlyList<string> targetNames)
            => $"{spellName} {AppliedPhrase(effect, JoinNames(targetNames))}";

        /// <summary>
        /// #293 — the report for a <see cref="Effect.MoraleTestThen"/> spell, whose targets each took a
        /// morale test and only the failures were affected. One line for the whole spell, so a
        /// multi-target cast is a single banner rather than one per unit.
        /// </summary>
        public static string DescribeConditionalApplied(string spellName, Effect onFailure,
            IReadOnlyList<string> failedNames, IReadOnlyList<string> passedNames)
        {
            if (failedNames.Count == 0)
            {
                return $"{spellName}: {JoinNames(passedNames)} passed the morale test - no effect";
            }

            string applied = DescribeApplied(spellName, onFailure, failedNames);
            return passedNames.Count == 0
                ? $"{applied} (morale test failed)"
                : $"{applied} (morale test failed); {JoinNames(passedNames)} passed";
        }

        // The verb phrase, with the affected units already folded in - "grants Rending to X (this round)".
        // Deliberately mirrors DescribeEffect's coverage; anything unrecognised degrades to a truthful
        // "affected X" rather than inventing detail.
        private static string AppliedPhrase(Effect effect, string targets)
        {
            switch (effect)
            {
                case Effect.DealHits dealHits:
                    // Present tense, unlike its siblings: the damage banner is emitted BEFORE the child
                    // pipeline rolls the hits, so "dealt" would claim an outcome that has not happened.
                    // The type rides along ("(AP(1), Rending)") - the same modifier list the picker shows,
                    // because "3 hits" alone does not tell the player what is about to land.
                    return $"deals {dealHits.Count} hit{Plural(dealHits.Count)}" +
                           $"{DescribeHitModifiers(dealHits)} to {targets}";
                case Effect.AddRule addRule:
                    return $"grants {addRule.RuleName} to {targets} ({DescribeLifetime(addRule.Scope)})";
                case Effect.StatModifier statMod:
                {
                    string sign = statMod.Delta >= 0 ? "+" : "";
                    return $"gives {targets} {sign}{statMod.Delta} to {RollName(statMod.Roll)} " +
                           $"({DescribeLifetime(statMod.LifetimeScope)})";
                }
                case Effect.TriggeredMove move:
                    return $"moved {targets} up to {Inches(move.MaxInches)}\"";
                case Effect.ApplyFatigue:
                    return $"left {targets} fatigued";
                case Effect.MarkTarget mark:
                    return $"marked {targets} - the next friendly to attack gets {mark.RuleName}";
                case Effect.MoraleTestThen conditional:
                    // Only reached if a caller bypasses DescribeConditionalApplied; report the branch
                    // rather than the wrapper so the line still says something true.
                    return $"forced a morale test on {targets}; on a fail, " +
                           $"{DescribeEffect(conditional.OnFailure)}";
                default:
                    return $"affected {targets}";
            }
        }

        /// <summary>
        /// What KIND of hits a damage spell deals: " (AP(1), Rending)", or "" for plain hits. Shared by
        /// the picker's advertisement and the #293 result banner so the player is told the same thing
        /// twice in the same words.
        /// </summary>
        private static string DescribeHitModifiers(Effect.DealHits dealHits)
        {
            List<string> modifiers = new List<string>();
            if (dealHits.ArmorPenetration > 0) modifiers.Add($"AP({dealHits.ArmorPenetration})");
            modifiers.AddRange(dealHits.WithRules);
            return modifiers.Count > 0 ? $" ({string.Join(", ", modifiers)})" : "";
        }

        /// <summary>"X", "X and Y", "X, Y and Z" - internal for tests.</summary>
        internal static string JoinNames(IReadOnlyList<string> names) => names.Count switch
        {
            0 => "nothing",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
        };

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

        private static string RollName(ERollKind roll) => roll switch
        {
            ERollKind.Hit => "hit rolls",
            ERollKind.Save => "defense rolls",
            ERollKind.Morale => "morale",
            ERollKind.Cast => "casting rolls",
            _ => "rolls",
        };

        private static string Plural(int count) => count == 1 ? "" : "s";

        private static string Inches(float value) => value % 1f == 0f ? ((int)value).ToString() : value.ToString("0.#");
    }
}
