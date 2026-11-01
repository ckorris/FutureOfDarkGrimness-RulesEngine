using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #431: wounds a unit COUNTS as having taken this activation without losing them - OPR 3.6 Scourge ("For each
/// unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when checking who won
/// melee, or if shooting wounds would trigger Morale Tests"). The one ledger between the save roll that earns
/// them (<c>AssignWoundsStage</c>, folding <see cref="Definitions.RuleOperation.AddCountedWoundsTaken"/>) and
/// the two checks that read them: the melee result (<c>DetermineMeleeWinnerStage</c>) and the wound-driven
/// morale test of a shooting attack (<c>ResolveRangedMoraleStage</c>, <c>ResolveStrafeMoraleStage</c>).
///
/// <para>Kept as a <see cref="TokenType.CountedWoundsTaken"/> magnitude on the STRUCK unit rather than on a
/// combat context: the charger's swings and the defender's strike-back run under different contexts, and the
/// per-weapon combat metadata cannot reach the action context at all, while both combatants' tokens are in
/// reach of every stage. Readers <see cref="Take"/> (read and remove), and the end of every activation
/// <see cref="SweepAll"/>s whatever a skipped check left behind (a melee that ended in a wipe-out never
/// reaches the result step), because "this activation" is the text's scope and the end-of-activation token
/// sweep only visits the activated unit.</para>
/// </summary>
public static class CountedWounds
{
    /// <summary> Adds <paramref name="count"/> counted wounds to <paramref name="unit"/> (merging magnitudes). </summary>
    public static void Add(IUnit unit, float count)
    {
        if (count <= 0f)
        {
            return;
        }

        unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.CountedWoundsTaken,
            payload: new TokenPayload.Magnitude(count)));
    }

    /// <summary> The counted wounds <paramref name="unit"/> carries, without removing them. </summary>
    public static float Peek(IUnit unit) => unit.Tokens.GetTokenMagnitude(TokenType.CountedWoundsTaken);

    /// <summary> Reads <paramref name="unit"/>'s counted wounds and removes them - a check has used them. </summary>
    public static float Take(IUnit unit)
    {
        float counted = Peek(unit);
        int tokens = unit.Tokens.GetTokenCount(TokenType.CountedWoundsTaken);
        if (tokens > 0)
        {
            unit.Tokens.RemoveTokens(TokenType.CountedWoundsTaken, tokens);
        }

        return counted;
    }

    /// <summary> Clears every unit's counted wounds - the activation they belonged to is over. </summary>
    public static void SweepAll(IEnumerable<IUnit> units)
    {
        foreach (IUnit unit in units)
        {
            int tokens = unit.Tokens.GetTokenCount(TokenType.CountedWoundsTaken);
            if (tokens > 0)
            {
                unit.Tokens.RemoveTokens(TokenType.CountedWoundsTaken, tokens);
            }
        }
    }
}
