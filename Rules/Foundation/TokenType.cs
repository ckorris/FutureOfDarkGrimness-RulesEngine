namespace FDG.Rules.Foundation;

public readonly record struct TokenType(string Id)
{
    public const string SHAKEN_ID = "Shaken";
    public const string FATIGUED_ID = "Fatigued";
    public const string SPELL_TOKENS_ID = "SpellTokens";
    public const string ACCUMULATOR_TOKENS_ID = "AccumulatorTokens";
    public const string RULE_GRANT_ID = "RuleGrant";
    public const string ARRIVED_FROM_RESERVE_ID = "ArrivedFromReserve";
    public const string IN_RESERVE_ID = "InReserve";
    public const string EMBARKED_IN_ID = "EmbarkedIn";
    public const string MARK_ID = "Mark";
    public const string POST_COMBAT_MOVE_USED_ID = "PostCombatMoveUsed";
    public const string DELAYED_ACTION_USED_ID = "DelayedActionUsed";
    public const string OFF_TABLE_FROM_FORCED_MOVE_ID = "OffTableFromForcedMove";
    public const string LIMITED_SPENT_ID = "LimitedSpent";
    public const string PENDING_AMBUSH_ARRIVAL_ID = "PendingAmbushArrival";

    // Granted numeric roll modifiers (#033 stat-modifier primitive): a spell/ability grants the bearer a
    // signed delta to a specific roll for a duration. The roll kind is the token TYPE (so different rolls
    // never merge, and Foundation needn't reference ERollKind, which lives in Definitions); the payload
    // carries the delta.
    public const string HIT_ROLL_MODIFIER_ID = "HitRollModifier";
    public const string SAVE_ROLL_MODIFIER_ID = "SaveRollModifier";
    public const string MORALE_ROLL_MODIFIER_ID = "MoraleRollModifier";
    public const string CAST_ROLL_MODIFIER_ID = "CastRollModifier";

    // Attacker-bonus markers (#100 #14b, the Tag/Target/Spotter family): tokens sitting on an ENEMY
    // unit that make friendly attacks against it better. The bonus kind is the token TYPE (mirroring
    // the roll-modifier family above); each marker is worth +1. "Spendable" markers are claimed by the
    // attacking player before the roll — TargetMarkerSpend prompts how many to remove and each removed
    // marker buys +1 for that roll only. "Persistent" markers are never consumed: every friendly attack
    // gets +count while they sit (the Target family's "friendly units get +X when attacking it").
    public const string SPENDABLE_HIT_BONUS_ID = "SpendableHitBonusMarker";
    public const string SPENDABLE_AP_BONUS_ID = "SpendableApBonusMarker";
    public const string PERSISTENT_HIT_BONUS_ID = "PersistentHitBonusMarker";
    public const string PERSISTENT_AP_BONUS_ID = "PersistentApBonusMarker";


    public static readonly TokenType Shaken = new(SHAKEN_ID);
    public static readonly TokenType Fatigued = new(FATIGUED_ID);
    public static readonly TokenType SpellTokens = new(SPELL_TOKENS_ID);

    /// <summary>
    /// A lending pool a unit builds up for OTHER friendly casters to draw on (Spell Accumulator). Spent
    /// "as if they were their own spell tokens", so functionally identical to
    /// <see cref="SpellTokens"/> at the point of spending — but a SEPARATE type, and that separation is
    /// load-bearing rather than tidy-minded:
    /// <list type="bullet">
    ///   <item>the rule reads "casters from OTHER friendly units may spend this model's accumulator
    ///         tokens". An accumulator that is itself a caster (the corpus puts Change Boon on caster
    ///         units) must not be able to spend its own pool - which is exactly what one shared type
    ///         would allow;</item>
    ///   <item>its cap is the rule's ("can't hold more than 6 at once"), not the engine's
    ///         <c>MAX_SPELL_TOKENS</c>, and the two are free to diverge;</item>
    ///   <item>holding a lending pool must never make a unit look like a caster to the #103 assist scan
    ///         or to any other "has spell tokens" test.</item>
    /// </list>
    /// Which pools a unit lends, to whom, and how far is not read off this type: it is answered at
    /// <see cref="EHookID.Lifecycle_OnCapabilityQuery"/> and gathered by <c>SpellPurse</c>.
    /// </summary>
    public static readonly TokenType AccumulatorTokens = new(ACCUMULATOR_TOKENS_ID);
    public static readonly TokenType RuleGrant = new (RULE_GRANT_ID);
    public static readonly TokenType HitRollModifier = new(HIT_ROLL_MODIFIER_ID);
    public static readonly TokenType SaveRollModifier = new(SAVE_ROLL_MODIFIER_ID);
    public static readonly TokenType MoraleRollModifier = new(MORALE_ROLL_MODIFIER_ID);
    public static readonly TokenType CastRollModifier = new(CAST_ROLL_MODIFIER_ID);
    public static readonly TokenType SpendableHitBonus = new(SPENDABLE_HIT_BONUS_ID);
    public static readonly TokenType SpendableApBonus = new(SPENDABLE_AP_BONUS_ID);
    public static readonly TokenType PersistentHitBonus = new(PERSISTENT_HIT_BONUS_ID);
    public static readonly TokenType PersistentApBonus = new(PERSISTENT_AP_BONUS_ID);

    /// <summary>
    /// Marks a unit that arrived from reserve (Ambush) this round. Engine-known because
    /// <c>ReconcileObjectivesStage</c> reads it to exclude the newcomer from seizing/contesting
    /// objectives the round it arrives. Granted with a <c>RoundEnd</c> clear trigger, so the
    /// round-end token sweep removes it after that round's objective check.
    /// </summary>
    public static readonly TokenType ArrivedFromReserve = new(ARRIVED_FROM_RESERVE_ID);

    /// <summary>
    /// Marks a unit the player held back at deployment for a later-round arrival (Ambush): it is off the
    /// table and cannot be activated, targeted, or drawn until it arrives.
    ///
    /// Before this token existed, "in reserve" was inferred from every model sitting at the world origin,
    /// a rule re-derived independently in the activation pool, two `IsUnplaced` copies, the renderer, the
    /// AI, and the line-of-sight builder. That made reserve status a property of a position rather than of
    /// the unit: anything that wrote a reserve model's position - even writing (0,0) back onto it - made
    /// the unit look deployed, which is how a held-back unit turned up activatable in round 1 and drawn in
    /// the table's bottom-left corner.
    ///
    /// Carried with a <c>ManualOnly</c> clear trigger: it must survive the round-end sweep, and
    /// <c>StartOfRoundExtraActionStage</c> removes it explicitly when the unit arrives.
    /// </summary>
    public static readonly TokenType InReserve = new(IN_RESERVE_ID);

    /// <summary>
    /// #029: marks an Aircraft that flew off the table edge during its forced move. It's held off the table
    /// (models at origin) until <c>StartOfRoundExtraActionStage</c> redeploys it from a board edge at the next
    /// round start, which clears this token. Carried with a <c>ManualOnly</c> clear trigger (cleared explicitly
    /// on redeploy), not the round-end sweep.
    /// </summary>
    public static readonly TokenType OffTableFromForcedMove = new(OFF_TABLE_FROM_FORCED_MOVE_ID);

    /// <summary>
    /// #032 Limited: marks that a model has fired a once-per-game weapon. Lives on the MODEL (not the unit or
    /// weapon — weapons have no token container), with a <see cref="Tokens.TokenPayload.WeaponName"/> payload
    /// naming the spent weapon, so a model carrying two different Limited weapons tracks them independently and
    /// casualties drop the spent-state with the model. Carried with a <c>ManualOnly</c> clear (it must survive
    /// the round-end sweep and last the whole game). Count = times fired (1 for plain Limited; X-ready).
    /// </summary>
    public static readonly TokenType LimitedSpent = new(LIMITED_SPENT_ID);

    /// <summary>
    /// #197 P22 Ambush Re-Deployment: the unit removed itself at the end of an activation and MUST
    /// redeploy as if it had Ambush at the start of the next round (owner-ruled mandatory,
    /// 2026-07-28). The rule's <c>deferDeployment</c> entry is gated on this token, so the arrival
    /// pass finds a defer for the reserved unit exactly while the return is pending; cleared on
    /// arrival. ManualOnly - it must survive the round-end sweep between removal and return.
    /// </summary>
    public static readonly TokenType PendingAmbushArrival = new(PENDING_AMBUSH_ARRIVAL_ID);

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
    /// #197 Delayed Action: per-round, per-player marker that a player has already used their once-per-round
    /// "hold back" (pass the turn without activating). Placed on the unit that was held back; scanned across
    /// the player's own living units before offering the option again. Carried with a <c>RoundEnd</c> clear
    /// trigger, swept by the round-end token pass (same lifecycle as <see cref="Fatigued"/>).
    /// </summary>
    public static readonly TokenType DelayedActionUsed = new(DELAYED_ACTION_USED_ID);

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