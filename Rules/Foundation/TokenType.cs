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
    public const string JOINS_ROUND_IN_PROGRESS_ID = "JoinsRoundInProgress";
    public const string REINFORCEMENT_SPENT_ID = "ReinforcementSpent";
    public const string PENDING_REINFORCEMENT_ARRIVAL_ID = "PendingReinforcementArrival";

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

    // #197 Inquisitorial Agent: stamped on a unit that spent a quota-gated reactivation this round, so the
    // army-wide "only up to one third of them per round" cap can count the uses. Cleared at round end -
    // the cap is per round, while the unit's own once-per-game gate is a separate, permanent marker.
    public const string REACTIVATED_THIS_ROUND_ID = "ReactivatedThisRound";

    // #197 P19 - the turn-order primitive, deliberately named for the MECHANISM rather than for any rule
    // that uses it (Coordinate is the first, and the engine never mentions it).
    //
    // ACTIVATES_NEXT sits on a unit that takes the NEXT activation, ahead of the normal team alternation:
    // DeterminePlayerTurnStage points the cursor at its owner instead of advancing, and
    // ChooseUnitToActivateStage activates it without a menu. The pool stays authoritative - a flag on a
    // unit that is not actually unactivated is ignored - so a stale token can only ever be a no-op.
    //
    // ACTIVATED_OUT_OF_ORDER records that an activation was GRANTED that way. It exists so an anti-chain
    // clause is authored as data (Not(TokenPresent(...))) rather than coded: Coordinate's "may not be used
    // if this unit was activated via Coordinate" is exactly that condition.
    //
    // ACTIVATED_THIS_ROUND is the plain fact "this unit has already activated this round", which was
    // previously derivable ONLY from the round context's pool - unreachable from a rule, an ability's
    // targeting, or any stage below the round layer. Stamped and cleared in lockstep with the pool by
    // SingleRoundContext (MarkUnitAsActivated / ReinstateUnitForActivation).
    public const string ACTIVATES_NEXT_ID = "ActivatesNext";
    public const string ACTIVATED_OUT_OF_ORDER_ID = "ActivatedOutOfOrder";
    public const string ACTIVATED_THIS_ROUND_ID = "ActivatedThisRound";

    // #197 Instinctive: "WHEN THIS MODEL IS ACTIVATED, if it is able to shoot/charge ... it must
    // immediately attack the closest valid target and gets +1 to hit for that attack." The condition is
    // read ONCE, when the unit activates - a unit that could not attack then is unconstrained for the
    // whole activation, even if its ordinary move brings an enemy into range. That decision has to
    // outlive the menu (the target choosers and the +1 rider both need it, and both run later), so
    // ChooseActionStage stamps this token on its first visit of a compelled activation and it clears at
    // end of activation. Its presence is the whole answer: the choosers gate on it, and the rider is
    // authored as Condition.TokenPresent over it, so a unit that merely HAS the rule gets nothing.
    public const string COMPELLED_TO_ATTACK_ID = "CompelledToAttack";

    // #197 Mobile Artillery's defensive arm: "as long as this unit hasn't moved during the ROUND, enemies
    // shooting it from over 9in get -2 to hit". Named for the FACT, not the rule, like the P19 family.
    //
    // Round-persistent because none of the existing "did it move" state can answer this question at the
    // seam that asks it. The hit hook fires during the ENEMY's activation, where UnitActionContext.HasMoved
    // is per-activation and about the ACTING unit, and Condition.AfterMoving reads the attacker, not the
    // bearer. So the fact is stamped on the unit itself and read as Not(TokenPresent(...)).
    //
    // Stamped at the one seam where a declared move has actually resolved (MovementStage's reconcile, next
    // to RegisterMoveFinished, which a backed-out move never reaches). Deliberately NOT stamped by a
    // Charge: charging requires the unit to already BE within melee range, so a charge is never itself a
    // move - whatever brought it into range was, and that stamped this. A unit that starts its activation
    // in contact and charges without moving has genuinely not moved, and keeps the bonus.
    public const string MOVED_THIS_ROUND_ID = "MovedThisRound";


    public static readonly TokenType Shaken = new(SHAKEN_ID);
    public static readonly TokenType Fatigued = new(FATIGUED_ID);
    public static readonly TokenType ReactivatedThisRound = new(REACTIVATED_THIS_ROUND_ID);
    public static readonly TokenType ActivatesNext = new(ACTIVATES_NEXT_ID);
    public static readonly TokenType ActivatedOutOfOrder = new(ACTIVATED_OUT_OF_ORDER_ID);
    public static readonly TokenType ActivatedThisRound = new(ACTIVATED_THIS_ROUND_ID);
    public static readonly TokenType CompelledToAttack = new(COMPELLED_TO_ATTACK_ID);
    public static readonly TokenType MovedThisRound = new(MOVED_THIS_ROUND_ID);
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
    /// #197 P17: a unit created MID-ROUND (Spawn/Split) that may still activate this round
    /// (owner-ruled 2026-07-28). The per-round activation pool snapshots at round start, so the
    /// creation service stamps this and <c>SingleRoundContext.AdoptMidRoundUnits</c> — run at the
    /// pool's own query seams, which is what lets the destruction-seam path reach it too — folds the
    /// unit in and clears the token. The round-start snapshot also sweeps strays, so a token that
    /// somehow survives to the next round grants nothing twice. ManualOnly: adoption owns removal.
    /// </summary>
    public static readonly TokenType JoinsRoundInProgress = new(JOINS_ROUND_IN_PROGRESS_ID);

    /// <summary>
    /// #197 P17c Reinforcement: the ORIGINAL unit accepted its reinforcement (its copy is queued), so
    /// the rule must not fire again — above all when the Shaken arm's removal-as-destroyed lands on the
    /// destruction seam, where the rule's own destroyed-arm entry would otherwise re-prompt. Both
    /// authored entries gate on this token's absence; the service also guards. ManualOnly.
    /// </summary>
    public static readonly TokenType ReinforcementSpent = new(REINFORCEMENT_SPENT_ID);

    /// <summary>
    /// #197 P17c: the COPY a Reinforcement queued, held in reserve until
    /// <c>StartOfRoundExtraActionStage</c> places it "at the beginning of the next round after
    /// Ambushers have been deployed" — mandatorily (the "you may" was spent at removal), within 12" of
    /// any table edge. Cleared on arrival. ManualOnly — it must survive the round-end sweep.
    /// </summary>
    public static readonly TokenType PendingReinforcementArrival = new(PENDING_REINFORCEMENT_ARRIVAL_ID);

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