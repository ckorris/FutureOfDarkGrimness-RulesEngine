using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #441 F5 / F7: what an "Enhanced X" rule depends on, read off its own definition. OPR 3.6's Enhanced family is
/// authored as an increment gated on the bearer carrying its base rule (<see cref="Condition.UnitHasRule"/> /
/// <see cref="Condition.WeaponHasRule"/> inside the definition - #433 Option B), so the base is the gate, never
/// the name: "Enhanced Lustbound Aura" has no gate of its own (it grants) and depends on nothing, and a rule
/// named after something other than its base still reads right. Gates under a <see cref="Condition.Not"/> are not
/// dependencies.
///
/// <para>Two shipped texts (Enhanced Watchborn, Enhanced Vinci Tech; Enhanced Versatile Warrior in the supplement)
/// gate on the base rule's one-activation HELPERS ("Watchborn (Piercing)", "Watchborn (Precision)") - the names
/// Watchborn's activation pick grants to its own unit for the activation - because that is what the Enhanced rule
/// lifts. The unit carries Watchborn, not the helpers, so a helper folds to the rule whose Self-targeted activated
/// ability grants it (<see cref="GrantingRule"/>), when the candidate set holds one. A grant aimed at ANOTHER unit
/// ("Furious Buff" hands Furious to a friend) is a separate rule, not the base, and does not fold: "Enhanced
/// Furious" would still depend on Furious. Without candidates (no resolver in reach) the gate name stands.</para>
///
/// <para><see cref="BaseRuleLine"/> is the display half (F7, signed off B): an Enhanced rule whose OPR text never
/// names its base - the two above read "If this model has Versatile Attack ... from Versatile Attack" (3.6.0
/// verbatim, #436 D3) - gets one display-only line, "Base rule: Watchborn.", appended under the text by
/// <see cref="HoverDescription"/> on every rule-hover surface. The text itself is never changed; a rule whose text
/// names its base (every other shipped Enhanced rule) gets no line.</para>
/// </summary>
public static class EnhancedRuleGate
{
    /// <summary>The 3.6 family's naming: every member is "Enhanced " + its base (plus an Aura/Buff suffix).</summary>
    public const string Prefix = "Enhanced ";

    /// <summary>
    /// The rule names the definition gates on the bearer carrying: <see cref="Condition.UnitHasRule"/> and
    /// <see cref="Condition.WeaponHasRule"/> anywhere in its passive entries' conditions or its activated
    /// abilities' availability, through And/Or, not under Not, and not the rule itself; first-seen order, distinct.
    /// </summary>
    public static IReadOnlyList<string> GateRuleNames(SpecialRuleDefinition definition)
    {
        List<string> names = new List<string>();
        foreach (HookEntry entry in definition.Passive ?? Array.Empty<HookEntry>())
        {
            Collect(entry.Condition, names);
        }

        foreach (ActivatedAbility ability in definition.Activated ?? Array.Empty<ActivatedAbility>())
        {
            Collect(ability.AvailableWhen, names);
        }

        return names
            .Where(n => !Same(n, definition.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The rule among <paramref name="candidates"/> whose Self-targeted activated ability grants
    /// <paramref name="helperName"/> (<see cref="Effect.AddRule"/>) - the base rule of a one-activation helper -
    /// or null when none does. A grant aimed at another unit (a Buff) is not a match.
    /// </summary>
    public static SpecialRuleDefinition? GrantingRule(string helperName, IEnumerable<SpecialRuleDefinition> candidates)
    {
        foreach (SpecialRuleDefinition candidate in candidates)
        {
            if (Same(candidate.Name, helperName)) continue;
            foreach (ActivatedAbility ability in candidate.Activated ?? Array.Empty<ActivatedAbility>())
            {
                if (ability.TargetSelector is { TargetAffinity: ETargetAffinity.Self }
                    && ability.Effect is Effect.AddRule grant
                    && Same(grant.RuleName, helperName))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The base rules of an Enhanced definition: its gate names, each helper folded to the rule in
    /// <paramref name="candidates"/> that grants it (the gate name itself when there is none, or when
    /// <paramref name="candidates"/> is null); first-seen order, distinct. Empty when the definition has no gate.
    /// </summary>
    public static IReadOnlyList<string> BaseRuleNames(SpecialRuleDefinition definition,
        IEnumerable<SpecialRuleDefinition>? candidates) =>
        Bases(definition, candidates).Select(b => b.Base).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <inheritdoc cref="BaseRuleNames(SpecialRuleDefinition, IEnumerable{SpecialRuleDefinition})"/>
    public static IReadOnlyList<string> BaseRuleNames(SpecialRuleDefinition definition, IRuleResolver? resolver) =>
        BaseRuleNames(definition, resolver?.Definitions);

    /// <summary>
    /// The F7 line - "Base rule: Watchborn." ("Base rules: X and Y." for several) - for an Enhanced rule whose
    /// description names neither a base rule nor the gate name it stands for (case-insensitive); null for any
    /// other rule, a rule without a description, or one whose text already names every base.
    /// </summary>
    public static string? BaseRuleLine(SpecialRuleDefinition definition, IEnumerable<SpecialRuleDefinition>? candidates)
    {
        if (!definition.Name.StartsWith(Prefix, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(definition.Description))
        {
            return null;
        }

        List<string> unnamed = new List<string>();
        foreach ((string baseName, string gate) in Bases(definition, candidates))
        {
            if (definition.Description.Contains(baseName, StringComparison.OrdinalIgnoreCase)
                || definition.Description.Contains(gate, StringComparison.OrdinalIgnoreCase)
                || unnamed.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            unnamed.Add(baseName);
        }

        return unnamed.Count switch
        {
            0 => null,
            1 => $"Base rule: {unnamed[0]}.",
            _ => $"Base rules: {string.Join(" and ", unnamed)}.",
        };
    }

    /// <inheritdoc cref="BaseRuleLine(SpecialRuleDefinition, IEnumerable{SpecialRuleDefinition})"/>
    public static string? BaseRuleLine(SpecialRuleDefinition definition, IRuleResolver? resolver) =>
        BaseRuleLine(definition, resolver?.Definitions);

    /// <summary>
    /// The text a rule hover shows for <paramref name="definition"/>: its <see cref="SpecialRuleDefinition.Description"/>
    /// as authored, plus the <see cref="BaseRuleLine"/> on its own line when there is one. The one composition
    /// point for every surface that explains a rule, so the line reads the same everywhere.
    /// </summary>
    public static string HoverDescription(SpecialRuleDefinition definition, IEnumerable<SpecialRuleDefinition>? candidates)
    {
        string? line = BaseRuleLine(definition, candidates);
        return line == null ? definition.Description : definition.Description + "\n" + line;
    }

    /// <inheritdoc cref="HoverDescription(SpecialRuleDefinition, IEnumerable{SpecialRuleDefinition})"/>
    public static string HoverDescription(SpecialRuleDefinition definition, IRuleResolver? resolver) =>
        HoverDescription(definition, resolver?.Definitions);

    private static IEnumerable<(string Base, string Gate)> Bases(SpecialRuleDefinition definition,
        IEnumerable<SpecialRuleDefinition>? candidates)
    {
        foreach (string gate in GateRuleNames(definition))
        {
            SpecialRuleDefinition? granter = candidates == null ? null : GrantingRule(gate, candidates);
            yield return (granter != null && !Same(granter.Name, definition.Name) ? granter.Name : gate, gate);
        }
    }

    private static void Collect(Condition? condition, List<string> names)
    {
        switch (condition)
        {
            case Condition.UnitHasRule unitHas:
                names.Add(unitHas.RuleName);
                break;
            case Condition.WeaponHasRule weaponHas:
                names.Add(weaponHas.RuleName);
                break;
            case Condition.And and:
                Collect(and.Left, names);
                Collect(and.Right, names);
                break;
            case Condition.Or or:
                Collect(or.Left, names);
                Collect(or.Right, names);
                break;
            case Condition.Not:
                break; // a negative gate is not a dependency
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
