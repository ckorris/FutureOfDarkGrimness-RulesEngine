using System;
using System.Collections.Generic;
using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// Single source of truth for engine-known token TYPES (#151, Option B). Maps a <see cref="TokenType"/>
/// to its <see cref="TokenDefinition"/> (gameplay defaults + display), and provides <see cref="Create"/>
/// so a fixed-lifetime token is constructed from one place rather than re-typing its clear trigger at
/// every call site (the bug Option B retires — Shaken's ManualOnly was previously typed out in two files).
///
/// Unknown types — including data-driven custom types (#087) until they register here — fall through to a
/// safe default (Neutral / Normal / visible).
/// </summary>
public static class TokenDefinitionCatalog
{
    /// <summary>
    /// Prefix of the once-per-X cost markers minted by <c>RuleEvaluator.UsedMarker</c>
    /// ("AbilityUsed:&lt;rule&gt;"). All such markers share one Invisible definition — their lifetime is
    /// set by the ability's cost, not the type, so they carry no default trigger.
    /// </summary>
    public const string AbilityUsedPrefix = "AbilityUsed:";

    private static readonly TokenDefinition AbilityUsedDefinition = new(
        Id: AbilityUsedPrefix, Name: "Ability used", Valence: EValence.Neutral,
        Prominence: ETokenProminence.Invisible,
        Description: "Bookkeeping: this unit has used a once-per-activation / round / game ability.");

    private static readonly Dictionary<string, TokenDefinition> _byId = Build();

    /// <summary>The definition for <paramref name="type"/>, or a safe default for unknown/data-driven types.</summary>
    public static TokenDefinition Lookup(TokenType type) => Lookup(type.Id);

    /// <inheritdoc cref="Lookup(TokenType)"/>
    public static TokenDefinition Lookup(string id)
    {
        if (_byId.TryGetValue(id, out TokenDefinition? def))
        {
            return def;
        }

        if (id.StartsWith(AbilityUsedPrefix, StringComparison.Ordinal))
        {
            return AbilityUsedDefinition;
        }

        // Unknown / not-yet-registered data-driven type: visible, no strong valence, no fixed lifetime.
        return new TokenDefinition(id, id, EValence.Neutral, ETokenProminence.Normal);
    }

    /// <summary>
    /// Builds a token of <paramref name="type"/> using the catalog's default clear trigger — the single
    /// place a fixed-lifetime token's lifecycle lives. Instance data (count, payload, owner) is supplied by
    /// the caller; pass <paramref name="clearOverride"/> only for a deliberate exception. Throws if the type
    /// has no default trigger and none is supplied (a carrier type must pass its own, by design).
    /// </summary>
    public static Token Create(TokenType type, int count = 1, TokenPayload? payload = null,
        UnitID? owner = null, TokenClearTrigger? clearOverride = null)
    {
        TokenDefinition def = Lookup(type);
        TokenClearTrigger trigger = clearOverride ?? def.DefaultClearTrigger
            ?? throw new InvalidOperationException(
                $"Token type '{type.Id}' has no default clear trigger in {nameof(TokenDefinitionCatalog)}; " +
                "pass clearOverride explicitly (carrier types decide their lifetime at the grant site).");
        return new Token(type, count, trigger, payload, owner);
    }

    private static Dictionary<string, TokenDefinition> Build()
    {
        TokenDefinition[] entries =
        {
            new(TokenType.SHAKEN_ID, "Shaken", EValence.Negative, ETokenProminence.FirstClass,
                DefaultClearTrigger: new TokenClearTrigger.ManualOnly(),
                Description: "Shaken - spends its activation idle, cannot seize or contest objectives, and " +
                             "always fails morale tests until it recovers."),

            new(TokenType.FATIGUED_ID, "Fatigued", EValence.Negative, ETokenProminence.FirstClass,
                DefaultClearTrigger: new TokenClearTrigger.RoundEnd(),
                Description: "Fatigued - hits only on unmodified 6s in melee for the rest of the round."),

            new(TokenType.SPELL_TOKENS_ID, "Spell Tokens", EValence.Positive, ETokenProminence.FirstClass,
                ColorOverride: ETokenColor.Blue,
                Description: "Spell tokens available to spend on casting this round."),

            new(TokenType.HIT_ROLL_MODIFIER_ID, "Hit modifier", EValence.Neutral, ETokenProminence.Normal,
                ValenceSource: EValenceSource.PayloadSign),

            new(TokenType.SAVE_ROLL_MODIFIER_ID, "Defense modifier", EValence.Neutral, ETokenProminence.Normal,
                ValenceSource: EValenceSource.PayloadSign),

            new(TokenType.MORALE_ROLL_MODIFIER_ID, "Morale modifier", EValence.Neutral, ETokenProminence.Normal,
                ValenceSource: EValenceSource.PayloadSign),

            new(TokenType.RULE_GRANT_ID, "Granted rule", EValence.Neutral, ETokenProminence.Normal,
                ValenceSource: EValenceSource.GrantedRule),

            new(TokenType.MARK_ID, "Mark", EValence.Negative, ETokenProminence.Normal,
                DefaultClearTrigger: new TokenClearTrigger.ManualOnly(),
                Description: "Marked - a friendly attacker gains a rule against this unit until the first attack into it."),

            new(TokenType.ARRIVED_FROM_RESERVE_ID, "Arrived from reserve", EValence.Neutral,
                ETokenProminence.Invisible, DefaultClearTrigger: new TokenClearTrigger.RoundEnd()),

            // ManualOnly: reserve must survive the round-end sweep until the unit actually arrives.
            new(TokenType.IN_RESERVE_ID, "In reserve", EValence.Neutral,
                ETokenProminence.Invisible, DefaultClearTrigger: new TokenClearTrigger.ManualOnly()),

            new(TokenType.EMBARKED_IN_ID, "Embarked", EValence.Neutral,
                ETokenProminence.Invisible, DefaultClearTrigger: new TokenClearTrigger.ManualOnly()),

            new(TokenType.POST_COMBAT_MOVE_USED_ID, "Post-combat move used", EValence.Neutral,
                ETokenProminence.Invisible, DefaultClearTrigger: new TokenClearTrigger.RoundEnd()),

            new(TokenType.OFF_TABLE_FROM_FORCED_MOVE_ID, "Off table (forced move)", EValence.Neutral,
                ETokenProminence.Invisible, DefaultClearTrigger: new TokenClearTrigger.ManualOnly()),
        };

        var dict = new Dictionary<string, TokenDefinition>(StringComparer.Ordinal);
        foreach (TokenDefinition d in entries)
        {
            dict.Add(d.Id, d);
        }

        return dict;
    }
}
