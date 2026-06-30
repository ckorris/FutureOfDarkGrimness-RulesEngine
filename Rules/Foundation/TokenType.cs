namespace FDG.Rules.Foundation;

public readonly record struct TokenType(string Id)
{
    public const string SHAKEN_ID = "Shaken";
    public const string FATIGUED_ID = "Fatigued";
    public const string SPELL_TOKENS_ID = "SpellTokens";
    public const string RULE_GRANT_ID = "RuleGrant";
    public const string ARRIVED_FROM_RESERVE_ID = "ArrivedFromReserve";
    public const string EMBARKED_IN_ID = "EmbarkedIn";
    public const string MARK_ID = "Mark";
    public const string POST_COMBAT_MOVE_USED_ID = "PostCombatMoveUsed";
    public const string OFF_TABLE_FROM_FORCED_MOVE_ID = "OffTableFromForcedMove";
    public const string LIMITED_SPENT_ID = "LimitedSpent";
    public const string AIRCRAFT_HEADING_SET_ID = "AircraftHeadingSet";

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

    /// <summary>
    /// #029: marks an Aircraft that flew off the table edge during its forced move. It's held off the table
    /// (models at origin) until <c>StartOfRoundExtraActionStage</c> redeploys it from a board edge at the next
    /// round start, which clears this token. Carried with a <c>ManualOnly</c> clear trigger (cleared explicitly
    /// on redeploy), not the round-end sweep.
    /// </summary>
    public static readonly TokenType OffTableFromForcedMove = new(OFF_TABLE_FROM_FORCED_MOVE_ID);

    /// <summary>
    /// #029/#150: marks that an Aircraft has aimed its flight heading (set once, toward the table centre, on its
    /// first forced move) and stored it on every living model's <see cref="IModel.Facing"/>. While present,
    /// <c>ForcedAircraftMove.EnsureHeading</c> reads the heading back from the models instead of re-aiming —
    /// an Aircraft never turns. Cleared (<c>ManualOnly</c>) when the Aircraft flies off the table so it re-aims
    /// when it redeploys. Replaces the old nullable <c>UnitData.AircraftHeading</c>: the heading value now lives
    /// on the models, this token is just the "already aimed" signal.
    /// </summary>
    public static readonly TokenType AircraftHeadingSet = new(AIRCRAFT_HEADING_SET_ID);

    /// <summary>
    /// #032 Limited: marks that a model has fired a once-per-game weapon. Lives on the MODEL (not the unit or
    /// weapon — weapons have no token container), with a <see cref="Tokens.TokenPayload.WeaponName"/> payload
    /// naming the spent weapon, so a model carrying two different Limited weapons tracks them independently and
    /// casualties drop the spent-state with the model. Carried with a <c>ManualOnly</c> clear (it must survive
    /// the round-end sweep and last the whole game). Count = times fired (1 for plain Limited; X-ready).
    /// </summary>
    public static readonly TokenType LimitedSpent = new(LIMITED_SPENT_ID);

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

    /// <summary>
    /// Per-round marker that a unit has already made its post-combat move (Harassing / Hit & Run /
    /// Guerrilla family) this round. The post-combat-move rules are "once per round" but fire on every
    /// shoot action and every resolved melee, so <c>PostCombatMoveGate</c> sets this token when a unit
    /// actually repositions and skips the offer while it is present. Carried with a <c>RoundEnd</c> clear
    /// trigger, swept by the round-end token pass (the same lifecycle as <see cref="Fatigued"/>). One
    /// shared budget across shooting and melee — a unit moves at most once after combat per round.
    /// </summary>
    public static readonly TokenType PostCombatMoveUsed = new(POST_COMBAT_MOVE_USED_ID);

    /// <summary>
    /// A cross-unit "mark" placed on an enemy unit by a mark spell/ability (#100 #14). Carries a
    /// <c>TokenPayload.RuleGrant</c> naming the rule a friendly attacker gains against the marked enemy.
    /// Distinct from <see cref="RuleGrant"/> so the enemy doesn't read it as a buff on itself: it's read
    /// only at the attacker-side claim (<c>DetermineHitRollStage</c>), which transfers the named rule to the
    /// attacker as a one-attack grant and removes the mark — spent by the first attack into the enemy,
    /// regardless of the dice. Carried with a <c>ManualOnly</c> clear (removed explicitly on claim).
    /// </summary>
    public static readonly TokenType Mark = new(MARK_ID);
}