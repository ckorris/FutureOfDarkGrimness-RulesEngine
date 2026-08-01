using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// "Does anything this unit carries actually READ this token type?" — a static walk over the bearer's
/// attached rules (unit-level, per-model, and per-weapon), looking for a <see cref="Condition.TokenPresent"/>
/// naming the type anywhere in a hook's condition or an activated ability's availability gate.
///
/// <para>Exists for <see cref="Tokens.TokenDefinition.VisibleOnlyWhenRead"/> (#308): a bookkeeping token
/// that every unit gets stamped with is noise on the unit that has no rule caring about it, and genuine
/// information on the one that does. Rather than hard-coding "hide Moved", the catalog marks the type and
/// this decides per bearer — so a future book rule reading MovedThisRound lights the chip up for its unit
/// with no code change.</para>
///
/// <para>Deliberately CONDITIONS only, not effects. A rule that STAMPS or CLEARS a token is not a reader:
/// the movement stage stamps "Moved" on everything, and if that counted, nothing would ever be hidden.
/// What makes a token worth showing is that some rule's behaviour turns on it.</para>
/// </summary>
public static class TokenReadership
{
    /// <summary>
    /// Whether any rule attached to <paramref name="bearer"/>, its living-or-dead models, or their weapons
    /// tests <paramref name="type"/> in a condition. False for a null bearer — the caller cannot prove the
    /// token is unread, so callers treat that as "leave it visible".
    /// </summary>
    public static bool IsReadByAnyRule(IUnit? bearer, TokenType type)
    {
        if (bearer == null) return false;

        if (AnyRuleReads(bearer.RuleDefinitions, type)) return true;

        foreach (IModel model in bearer.Models)
        {
            if (AnyRuleReads(model.RuleDefinitions, type)) return true;

            foreach (Weapon weapon in model.Weapons)
            {
                if (AnyRuleReads(weapon.RuleDefinitions, type)) return true;
            }
        }

        return false;
    }

    private static bool AnyRuleReads(IReadOnlyList<ResolvedRule> rules, TokenType type)
    {
        foreach (ResolvedRule rule in rules)
        {
            foreach (HookEntry hook in rule.Definition.Passive)
            {
                if (Reads(hook.Condition, type)) return true;
            }

            foreach (ActivatedAbility ability in rule.Definition.Activated)
            {
                if (Reads(ability.AvailableWhen, type)) return true;
            }
        }

        return false;
    }

    // Recursive because the corpus wraps token tests in the boolean combinators - Mobile Artillery's
    // defensive arm is And(AttackedFromOverInches(9), Not(TokenPresent(MovedThisRound))), so a
    // top-level type check would miss every real use.
    private static bool Reads(Condition condition, TokenType type) => condition switch
    {
        Condition.TokenPresent present => present.TType == type,
        Condition.Not not              => Reads(not.Inner, type),
        Condition.And and              => Reads(and.Left, type) || Reads(and.Right, type),
        Condition.Or or                => Reads(or.Left, type) || Reads(or.Right, type),
        _                              => false,
    };
}
