using System.Collections.Generic;
using System.Reflection;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Computes how many arguments a <see cref="SpecialRuleDefinition"/> actually reads — the highest
/// <see cref="ValueSource.Arg"/> index reachable from its effects. Used at rule attach
/// (<c>ArmyListRuleResolution.ResolveForScope</c>) to catch a definition that reads <c>Arg(n)</c>
/// when the referencing army entry supplies fewer than n+1 arguments, *before* that mismatch becomes
/// a raw <see cref="System.IndexOutOfRangeException"/> in <see cref="ValueSource.Arg.Resolve"/>
/// mid-dispatch (#059 workstream 4).
///
/// Effects are flat (no effect nests another) and <see cref="ValueSource"/> only ever appears as a
/// direct property of an effect, so this reflects each effect's <see cref="ValueSource"/>-typed
/// properties rather than hand-listing the six arg-driven effects — a future seventh is covered
/// automatically. (It does not look inside a hypothetical <c>IReadOnlyList&lt;ValueSource&gt;</c>;
/// none exists today.)
/// </summary>
public static class RuleArgumentArity
{
    /// <summary>
    /// The highest argument index any <see cref="ValueSource.Arg"/> in <paramref name="definition"/>
    /// reads, or -1 if the definition reads no arguments. A definition needs at least
    /// <c>MaxReferencedArgIndex + 1</c> supplied arguments to bind without throwing at dispatch.
    /// </summary>
    public static int MaxReferencedArgIndex(SpecialRuleDefinition definition)
    {
        int max = -1;

        foreach (HookEntry entry in definition.Passive)
        {
            max = Max(max, MaxIndexInEffect(entry.Effect));
        }

        foreach (ActivatedAbility ability in definition.Activated)
        {
            max = Max(max, MaxIndexInEffect(ability.Effect));
        }

        return max;
    }

    private static int MaxIndexInEffect(Effect effect)
    {
        int max = -1;

        foreach (PropertyInfo property in effect.GetType().GetProperties())
        {
            if (!typeof(ValueSource).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            if (property.GetValue(effect) is ValueSource.Arg arg)
            {
                max = Max(max, arg.Index);
            }
        }

        return max;
    }

    private static int Max(int a, int b) => a > b ? a : b;
}
