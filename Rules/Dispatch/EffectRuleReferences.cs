using System.Collections.Generic;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #369 — the OTHER rules an effect names. A buff ability's description is written in terms of what it
/// confers ("...which gains Courage for its next relevant roll"), so the interesting rule is not the one
/// on the menu button but the one that rule hands out; a player reading the row has no way to learn what
/// Courage is without leaving the game.
///
/// <para>Deliberately derived from the EFFECT rather than by scanning the description text for anything
/// that looks like a rule name: half the catalog's names are ordinary English words ("Fast", "Tough",
/// "Devout"), so a text scan would underline prose. What the effect actually references is exact, and it
/// is the set worth explaining.</para>
///
/// <para>Names are returned as authored - the same strings <see cref="IRuleResolver"/> resolves and the
/// same ones the description spells out - in first-mention order, deduplicated.</para>
/// </summary>
public static class EffectRuleReferences
{
    /// <summary>
    /// Every rule name <paramref name="effect"/> grants, suppresses, marks with, or attaches to the hits
    /// it deals. Empty for the many effects that name none.
    /// </summary>
    public static IReadOnlyList<string> NamesIn(Effect? effect)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        Collect(effect, names, seen);
        return names;
    }

    private static void Collect(Effect? effect, List<string> names, HashSet<string> seen)
    {
        switch (effect)
        {
            case null:
                return;
            case Effect.AddRule addRule:
                Add(addRule.RuleName, names, seen);
                return;
            case Effect.Aura aura:
                Add(aura.RuleName, names, seen);
                return;
            case Effect.MarkTarget mark:
                Add(mark.RuleName, names, seen);
                return;
            case Effect.IgnoreRule ignore:
                Add(ignore.RuleName, names, seen);
                return;
            case Effect.DealHits deal:
                AddAll(deal.WithRules, names, seen);
                return;
            case Effect.ExtraAttack extra:
                AddAll(extra.WithRules, names, seen);
                return;
            case Effect.StormOfHits storm:
                AddAll(storm.WithRules, names, seen);
                return;
            // The only effect that WRAPS another one, so the only place recursion is needed today.
            case Effect.MoraleTestThen moraleTest:
                Collect(moraleTest.OnFailure, names, seen);
                return;
            default:
                return;
        }
    }

    private static void AddAll(IReadOnlyList<string>? rules, List<string> names, HashSet<string> seen)
    {
        if (rules == null) return;
        foreach (string rule in rules) Add(rule, names, seen);
    }

    private static void Add(string? name, List<string> names, HashSet<string> seen)
    {
        if (!string.IsNullOrWhiteSpace(name) && seen.Add(name!)) names.Add(name!);
    }
}
