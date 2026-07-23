using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Definitions;

/// <summary>
/// Reads a unit's <see cref="TokenType.RuleGrant"/> tokens from inside the rule vocabulary — for the
/// self-referential pieces (<see cref="Condition.AllModelsHaveThisRule"/>,
/// <see cref="ValueSource.RuleCarrierCount"/>) that ask "does this unit have the rule that is firing?"
/// and must count aura / "gains rule X" grants alongside statically attached rules.
///
/// Shared so those two agree by construction: a rule whose gate says every model has it and whose count
/// says how many do must not disagree about what "has" means.
/// </summary>
internal static class RuleGrantQueries
{
    /// <summary>
    /// True if the unit holds an aura/"gains rule X" grant for <paramref name="definition"/>. Granted rules
    /// are matched by canonical name — the alias-aware resolver the dispatcher uses for read-back isn't
    /// available in here, and core auras grant the canonical name.
    /// </summary>
    internal static bool UnitHasGrantedRule(IUnit unit, SpecialRuleDefinition definition)
    {
        foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
        {
            if (token.Payload is TokenPayload.RuleGrant grant
                && string.Equals(grant.RuleName, definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
