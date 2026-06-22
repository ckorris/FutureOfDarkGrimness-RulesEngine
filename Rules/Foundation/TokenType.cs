namespace FDG.Rules.Foundation;

public readonly record struct TokenType(string Id)
{
    public const string SHAKEN_ID = "Shaken";
    public const string FATIGUED_ID = "Fatigued";
    public const string SPELL_TOKENS_ID = "SpellTokens";
    public const string RULE_GRANT_ID = "RuleGrant";
    public const string ARRIVED_FROM_RESERVE_ID = "ArrivedFromReserve";

    // Granted numeric roll modifiers (#033 stat-modifier primitive): a spell/ability grants the bearer a
    // signed delta to a specific roll for a duration. The roll kind is the token TYPE (so different rolls
    // never merge, and Foundation needn't reference ERollKind, which lives in Definitions); the payload
    // carries the delta.
    public const string HIT_ROLL_MODIFIER_ID = "HitRollModifier";
    public const string SAVE_ROLL_MODIFIER_ID = "SaveRollModifier";
    public const string MORALE_ROLL_MODIFIER_ID = "MoraleRollModifier";


    public static readonly TokenType Shaken = new(SHAKEN_ID);
    public static readonly TokenType Fatigued = new(FATIGUED_ID);
    public static readonly TokenType SpellTokens = new(SPELL_TOKENS_ID);
    public static readonly TokenType RuleGrant = new (RULE_GRANT_ID);
    public static readonly TokenType HitRollModifier = new(HIT_ROLL_MODIFIER_ID);
    public static readonly TokenType SaveRollModifier = new(SAVE_ROLL_MODIFIER_ID);
    public static readonly TokenType MoraleRollModifier = new(MORALE_ROLL_MODIFIER_ID);

    /// <summary>
    /// Marks a unit that arrived from reserve (Ambush) this round. Engine-known because
    /// <c>ReconcileObjectivesStage</c> reads it to exclude the newcomer from seizing/contesting
    /// objectives the round it arrives. Granted with a <c>RoundEnd</c> clear trigger, so the
    /// round-end token sweep removes it after that round's objective check.
    /// </summary>
    public static readonly TokenType ArrivedFromReserve = new(ARRIVED_FROM_RESERVE_ID);
}