using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Resolves a <see cref="Token"/> into a display-ready <see cref="TokenDisplayInfo"/> (#151) — the single
/// place the GUI gets token presentation data, so no game logic leaks into the renderer. Valence and the
/// display identity come from the token's <see cref="TokenDefinition"/> (single source of truth) plus its
/// instance payload; hover text is synthesized here (the <c>SpellText</c> / <c>SightRuleLabel</c> pattern).
/// </summary>
public static class TokenDisplay
{
    /// <summary>
    /// The token's valence for its bearer. Driven by the catalog's <see cref="EValenceSource"/>: Fixed
    /// types use the definition's value; roll-modifier carriers use their StatModifier payload sign; rule
    /// grants use the granted rule's own valence (needs <paramref name="rules"/>; falls back to the
    /// definition's value if unavailable).
    /// </summary>
    public static EValence ResolveValence(Token token, IRuleResolver? rules)
    {
        TokenDefinition def = TokenDefinitionCatalog.Lookup(token.Type);
        switch (def.ValenceSource)
        {
            case EValenceSource.PayloadSign:
                if (token.Payload is TokenPayload.StatModifier sm)
                {
                    return sm.Delta > 0 ? EValence.Positive
                        : sm.Delta < 0 ? EValence.Negative
                        : EValence.Neutral;
                }
                return def.Valence;

            case EValenceSource.GrantedRule:
                if (token.Payload is TokenPayload.RuleGrant rg && rules != null
                    && rules.TryResolve(rg.RuleName, out ResolvedRule resolved))
                {
                    return resolved.Definition.Valence;
                }
                return def.Valence;

            default:
                return def.Valence;
        }
    }

    /// <summary>
    /// Stable identity for hashing + the chip's look. For grant-carrying tokens (RuleGrant, Mark) it's the
    /// granted rule name, so different granted rules get different chips; otherwise the token type id.
    /// </summary>
    public static string ResolveDisplayId(Token token)
        => token.Payload is TokenPayload.RuleGrant rg ? rg.RuleName : token.Type.Id;

    /// <summary>Short label for the chip / status entry.</summary>
    public static string DescribeName(Token token)
    {
        if (token.Payload is TokenPayload.StatModifier sm)
        {
            return $"{Signed(sm.Delta)} to {RollLabel(token.Type)}";
        }

        if (token.Payload is TokenPayload.RuleGrant rg)
        {
            return token.Type == TokenType.Mark ? $"Marked: {rg.RuleName}" : rg.RuleName;
        }

        return TokenDefinitionCatalog.Lookup(token.Type).Name;
    }

    /// <summary>Full hover line synthesized from the token's type, payload, count, and lifetime.</summary>
    public static string DescribeDetail(Token token, IRuleResolver? rules = null)
    {
        string lifetime = DescribeLifetime(token.ClearTrigger);

        if (token.Payload is TokenPayload.StatModifier sm)
        {
            return $"{Signed(sm.Delta)} to {RollLabel(token.Type)} rolls, {lifetime}.";
        }

        if (token.Payload is TokenPayload.RuleGrant rg)
        {
            if (token.Type == TokenType.Mark)
            {
                return $"Marked - a friendly attacker gains {rg.RuleName} against this unit until the first attack into it.";
            }

            string ruleDesc = RuleDescription(rg.RuleName, rules);
            return ruleDesc.Length == 0
                ? $"Gains {rg.RuleName}, {lifetime}."
                : $"Gains {rg.RuleName} - {ruleDesc} ({lifetime}).";
        }

        if (token.Type == TokenType.SpellTokens)
        {
            return $"{token.Count} spell token{(token.Count == 1 ? "" : "s")} to spend on casting this round.";
        }

        TokenDefinition def = TokenDefinitionCatalog.Lookup(token.Type);
        return string.IsNullOrEmpty(def.Description) ? $"{def.Name} ({lifetime})." : def.Description;
    }

    /// <summary>Plain-language phrase for how long a token lasts.</summary>
    public static string DescribeLifetime(TokenClearTrigger trigger) => trigger switch
    {
        TokenClearTrigger.ManualOnly => "until removed",
        TokenClearTrigger.RoundEnd => "this round",
        TokenClearTrigger.ActivationEnd => "this activation",
        TokenClearTrigger.AttackEnd => "this attack",
        TokenClearTrigger.FirstTrigger => "next time it applies",
        TokenClearTrigger.UnitDestroyed => "while this unit lives",
        TokenClearTrigger.OwnerDestroyed => "while its source lives",
        TokenClearTrigger.CustomHook => "until a later game event",
        _ => "",
    };

    /// <summary>The single entry point the GUI calls per token on a unit/model.</summary>
    public static TokenDisplayInfo Resolve(Token token, IRuleResolver? rules, bool isModelScoped = false)
    {
        TokenDefinition def = TokenDefinitionCatalog.Lookup(token.Type);
        return new TokenDisplayInfo(
            DisplayId: ResolveDisplayId(token),
            Name: DescribeName(token),
            Description: DescribeDetail(token, rules),
            Valence: ResolveValence(token, rules),
            Prominence: def.Prominence,
            Count: token.Count,
            IsModelScoped: isModelScoped,
            LifetimeText: DescribeLifetime(token.ClearTrigger),
            ColorOverride: def.ColorOverride,
            ShapeOverride: def.ShapeOverride,
            OwnerUnitID: token.OwnerUnitID);
    }

    private static string Signed(int delta) => delta >= 0 ? $"+{delta}" : delta.ToString();

    private static string RollLabel(TokenType type) =>
        type == TokenType.HitRollModifier ? "Hit"
        : type == TokenType.SaveRollModifier ? "Defense"
        : type == TokenType.MoraleRollModifier ? "Morale"
        : "roll";

    private static string RuleDescription(string ruleName, IRuleResolver? rules)
        => rules != null && rules.TryResolve(ruleName, out ResolvedRule resolved)
            ? resolved.Definition.Description.TrimEnd('.', ' ')
            : "";
}
