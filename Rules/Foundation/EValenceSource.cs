namespace FDG.Rules.Foundation;

/// <summary>
/// Where a token type's <see cref="EValence"/> comes from — declared on the token definition (single
/// source of truth, #151) so the resolver needn't hardcode type lists. Most types are <see cref="Fixed"/>;
/// the generic carriers derive valence per-instance because the same type spans both buff and debuff.
/// </summary>
public enum EValenceSource
{
    /// <summary>Valence is the definition's fixed value (Shaken = Negative, Spell Tokens = Positive, Mark = Negative).</summary>
    Fixed = 0,

    /// <summary>Valence is the sign of the token's StatModifier payload delta (+ → Positive, − → Negative). Roll-modifier carriers.</summary>
    PayloadSign = 1,

    /// <summary>Valence is the granted rule's own valence, looked up by the RuleGrant payload's rule name.</summary>
    GrantedRule = 2,
}
