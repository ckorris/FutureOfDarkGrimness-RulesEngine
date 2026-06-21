namespace FDG.Rules.Foundation;

public readonly record struct TokenType(string Id)
{
    public const string SHAKEN_ID = "Shaken";
    public const string FATIGUED_ID = "Fatigued";
    public const string SPELL_TOKENS_ID = "SpellTokens";
    public const string RULE_GRANT_ID = "RuleGrant";
    public const string ARRIVED_FROM_RESERVE_ID = "ArrivedFromReserve";
    public const string EMBARKED_IN_ID = "EmbarkedIn";


    public static readonly TokenType Shaken = new(SHAKEN_ID);
    public static readonly TokenType Fatigued = new(FATIGUED_ID);
    public static readonly TokenType SpellTokens = new(SPELL_TOKENS_ID);
    public static readonly TokenType RuleGrant = new (RULE_GRANT_ID);

    /// <summary>
    /// Marks a unit that arrived from reserve (Ambush) this round. Engine-known because
    /// <c>ReconcileObjectivesStage</c> reads it to exclude the newcomer from seizing/contesting
    /// objectives the round it arrives. Granted with a <c>RoundEnd</c> clear trigger, so the
    /// round-end token sweep removes it after that round's objective check.
    /// </summary>
    public static readonly TokenType ArrivedFromReserve = new(ARRIVED_FROM_RESERVE_ID);

    /// <summary>
    /// Marks a unit that is currently embarked inside a Transport (#035). A <b>cross-unit</b> token:
    /// it lives on the embarked unit, with <c>OwnerUnitID</c> pointing at the transport carrying it.
    /// Engine-known because the Transport core rule reads it to derive a transport's occupancy (the
    /// transport stores no list — its load is a query over these tokens), to keep occupants off the
    /// battlefield (their models stay at origin), and to resolve an embarked unit's effective position
    /// (the transport's). Carried with a <c>ManualOnly</c> clear trigger: disembark and destruction-
    /// spillout remove it explicitly, so the spillout logic runs <i>before</i> the link is cut rather
    /// than racing an automatic <c>OwnerDestroyed</c> sweep.
    /// </summary>
    public static readonly TokenType EmbarkedIn = new(EMBARKED_IN_ID);
}