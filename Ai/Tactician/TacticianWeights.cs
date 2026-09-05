namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Every tunable scalar in the Tactician's greedy policy, in one place (#191 A4 - "weights are
    /// named constants in one file; tuning is benchmark-driven and recorded"). Change the committed
    /// defaults only with a benchmark run attached to the commit. The float fields are STATIC, not
    /// const, so the FdgLab tuning harness can override them at process start (--weights, #191
    /// automated tuning); they are process-global and must never change once games are running -
    /// the committed defaults remain the shipped policy.
    /// </summary>
    public static class TacticianWeights
    {
        /// <summary>
        /// Sets a weight by field name (the FdgLab --weights plumbing). False for an unknown name
        /// or a non-float field. Call before any game starts - weights are process-global and read
        /// live by every planner.
        /// </summary>
        public static bool TrySet(string name, float value)
        {
            System.Reflection.FieldInfo? field = typeof(TacticianWeights).GetField(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (field == null || field.FieldType != typeof(float) || field.IsLiteral) return false;
            field.SetValue(null, value);
            return true;
        }

        // --- Activation-order urgency (A4-1) -------------------------------------------------------
        // score = KillOpportunity * (best value-weighted damage the unit can deal this activation)
        //       + ObjectiveFlip   * (it can reach and change an objective this activation)
        //       + UnderThreat     * (best value-weighted damage the enemy could deal IT)
        // Rationale: act with a unit before the opponent's next activation can remove it or its
        // opportunity; flips beat damage because objectives decide the winner.

        // TUNED 2026-07-10 after the A4-2 gate collapse (mirror avg 23.75% - ledger entry): the
        // objective terms were flat bonuses (2.0-2.5) while damage terms are value-FRACTIONS
        // (~0.0-0.5), so every unit rushed objectives and nothing ever fought back. One scale now:
        // a flip is worth a strong exchange, not ten of them.
        public static float ActivationKillOpportunity = 1.0f;
        public static float ActivationObjectiveFlip = 0.75f;
        public static float ActivationUnderThreat = 0.75f;

        // A5-6 (Chris): the round's delivery is boat-then-payload - a loaded transport acts early
        // (drive before the cargo decides) and embarked cargo acts late (after the boat has moved).
        public static float ActivationLoadedTransportBias = 0.5f;
        public static float ActivationEmbarkedCargoBias = -0.5f;

        // ADDED 2026-07-27 (#296, Chris's own crowded-game remedy: "activate the things in front
        // first, and move in a way that clears a path for whatever is behind"): a small bonus by
        // the unit's FORWARD PERCENTILE among this activation's valid options (0 = rearmost,
        // 1 = frontmost along the axis toward the enemy mass). Deliberately below every real
        // urgency signal - it only decides order when kill/flip/threat are flat, which is exactly
        // the crowded round-1 shape where rear units otherwise activate into sealed lanes and
        // drift sideways (the #256/#264 stuck-mob reports).
        public static float ActivationFrontlineBias = 0.1f;

        // ADDED 2026-09-05 (#191 step 10 P3, Chris: "most of my hard-earned wins against the bot
        // come down to my micro-managing of objective holding toward the end"). Activations
        // alternate, so in the LAST round the side that still has a marker-capable unit when the
        // other side has run out gets the last unopposed move. The flip term (act on the marker
        // NOW) is exactly backwards there: a responder that contests early gets shot off or
        // re-contested by the units the enemy still has. In the final round the flip term is
        // replaced by a tempo term - units that cannot reach any marker they could change are
        // spent first (+Spend), units that can are held for last (-Hold). The gap between the two
        // is deliberately above the flip bonus and every ordinary kill/threat fraction, so it
        // orders the round: irrelevant units first, responders last, kill/threat ordering within
        // each group. Lives in the ActivationScores the search's root priors come from, so A and B
        // both benefit (and the tree now opens "activate the irrelevant unit" at all).
        public static float ActivationLastRoundSpend = 0.5f;
        public static float ActivationLastRoundHold = 0.5f;

        // ADDED 2026-09-05 (#191 step 10 P0, Chris's GUI game: a unit partially on a marker assigned
        // its wounds to the models ON it and lost the marker): the stake a unit's presence on a
        // marker carries in wound assignment, in the resolver's output-value units (a rifleman is
        // 1.0, a heavy gunner ~3.5, a 10-shot AP2 autocannon 13). Round-scaled by ObjectiveUrgency
        // (x0.66 in round 1, x1.3 in the last) and split across the unit's models still standing on
        // the marker, so the LAST model on it is worth ~10 in round 1 and ~20 in the last round -
        // above any single model's gun. A fifth of this when another allied unit also stands there.
        public static float WoundObjectiveHold = 15f;

        // --- Action + movement choice (A4-2) --------------------------------------------------------
        // score = MoveDamage * (value-weighted damage from the endpoint; melee margin for charges)
        //       - MoveRetaliation * (best value-weighted damage an enemy can put on the endpoint)
        //       + MoveObjective   * (objectives newly held minus held objectives abandoned)
        //       + MoveReachableBonus when the candidate fully reaches its goal.
        // Objectives dominate deliberately: they decide the winner (house invariant), and the
        // baseline showed tie-heavy games from objective-blind play.

        public static float MoveDamage = 1.0f;
        // RETUNED 0.6 -> 0.45 (2026-07-10, Chris's hand-played game 1: "melee staying back"):
        // against a HUMAN gunline that holds its line, 0.6 x incoming swamped every forward term
        // for fragile units - his save showed Winged Grunts preferring FallBack (0.059) over
        // rushing an objective 23" out (0.039), and Guardians topping out on SeekCover. The solo
        // benchmark opponent advances into the horde, which masked the crossing problem entirely.
        // Exposure still prices (0.45 x a full volley beats most gradients) but no longer
        // dominates the reason a melee army exists: crossing the table.
        public static float MoveRetaliation = 0.45f;
        // REPLACED 2026-07-26 (#191 one-ply reply): the per-sharer dilution divisor priced the
        // enemy's volley by HEADCOUNT - two units in the envelope halved the bill no matter which
        // of them the enemy would actually shoot. Each enemy now models its best single reply:
        // we pay incoming x ours/(ours + best-alternative-target-value), so a juicy unit cannot
        // hide behind chaff and chaff pays little when a fatter target already shares the
        // envelope. The floor keeps a residual price on every exposed endpoint - our model of
        // the enemy's pick is approximate, and being wrong about "they'll shoot the other guy"
        // costs a whole unit.
        public static float RetaliationShareFloor = 0.25f;

        // ADDED 2026-07-26 (#191 idea 2, arriving pressure): enemies currently too far to answer
        // (zero priced retaliation) but one projected rush from threatening the endpoint next
        // round. Priced from a deterministic one-step projection - rush toward the nearest
        // attractive goal (a marker their side does not own, or one of our units) - at a
        // fraction of the real retaliation weight: it is a forecast, not a threat in being.
        // Projected MELEE pressure is skipped when we WANT that fight (positive melee margin
        // against the arriver): its approach is an opportunity, and the staged charge must not
        // be penalized for standing its ground.
        public static float MoveProjectedThreat = 0.15f;

        // #365 Tier 1, the wall-hugging reflex. REPLACED 2026-08-06 the #363 facet-3 scalar
        // (BlockedThreatShare), which discounted incoming fire when a wall cut the lane. That was
        // fact-math on a forecast: a boolean LoS test against where a shooter stands RIGHT NOW,
        // used to price what it does on its NEXT activation, after it moves. Being a boolean it
        // made a cliff in the score, and a cliff can only produce two behaviours - ignore cover or
        // hide in it. It cannot produce "take the slightly bent route", because bending a path 3"
        // does not change a boolean. Tuning it was tuning the height of a cliff (measured: 0.2
        // scored +0.78pp over 0.4, both inside the noise of maps that are 2.2% blocking terrain).
        //
        // Cover is a HABIT, not a plan (Chris): it shapes HOW a unit travels, never WHETHER it
        // pursues its goal. So threat is priced through walls again, and cover instead earns this
        // BOUNDED bonus on the share of enemy shooting that has no lane to the endpoint. Bounded
        // is the whole point - the term can never move a score by more than this, which makes
        // "never interrupts the goal" a property rather than a hope.
        //
        // Calibrated by pins, not taste. Chris's exchange rate - give up 2 of 12 inches of progress
        // for full cover, never give up 8 - brackets it, measured on the scene in
        // TacticianCoverHabitTests.ExchangeRateScene (all endpoints equidistant from the gunline,
        // so incoming fire is provably identical and only progress and cover differ):
        //
        //   2" of progress ....... 0.0229   -> the floor: below this the habit never bends a route
        //   8" of progress ....... 0.1526   -> the ceiling from the goal side
        //   a real 5-rifle volley  0.1686   -> the ceiling from the offense side (pin 11)
        //   MoveReachableBonus ... 0.0500   -> steps in when a candidate crosses zero, which
        //                                      tightens the practical ceiling to ~0.1026
        //
        // 0.05 sits at the geometric centre of (0.0229, 0.1026) - a little over 2x clear of both
        // ends. Note the bracket is a FRACTION of the route gap, not inches: giving up 2" when the
        // marker is 4" away is a much bigger deal than when it is 30" away, which is the behaviour
        // we want and comes free from ObjectiveApproach's normalisation.
        public static float MoveCoverHabit = 0.05f;

        // #365 Tier 2 (the lethality gate / veto) lived here from 2026-08-06 to 2026-08-06 and
        // was REMOVED the same day after failing its pool gate in every formulation tried: a
        // morale-knee curve over three threat aggregations (-4 to -14pp, monotone in perceived
        // threat) and a wipeout-only veto with ranked-decay aggregation (-4.06pp, z -3.11, worst
        // for the melee armies whose correct play is walking through near-lethal fire). The full
        // record - formulations, measurements, replay post-mortem, and the structural finding that
        // f(threat) x candidate-constant is just a second retaliation term in the argmax - is in
        // WorkItems/365-cover-as-a-habit.md. Do not reintroduce a goal-overriding threat term
        // without reading it first; the safe home for smarter caution is retaliation's curve.

        // --- Risk posture (#191 idea 3) -------------------------------------------------------------
        // ADDED 2026-07-26: the projected objective differential, round-scaled, tilts the risk
        // budget - 1-vs-3 on markers late must not score like 3-vs-1. Behind: retaliation and
        // arriving pressure discount (down to 1 - Relief at full deficit) and objective terms
        // boost (up to 1 + Boost) - the losing side buys variance. Ahead: retaliation prices UP
        // by the same relief slope - the winning side protects the lead and runs out the clock.
        // Round-scaled because an early deficit is deployment noise, a late one is the game.
        public static float PostureRetaliationRelief = 0.35f;
        public static float PostureObjectiveBoost = 0.3f;
        public static float MoveObjective = 0.75f;
        public static float MoveReachableBonus = 0.05f;

        // ADDED 2026-07-27 (#296, Chris: "you want a ball of units on the objective... close
        // behind is helpful for replacing the units in front of it when they die"): fraction of a
        // full marker step paid for ending in the SUPPORT band around a relevant marker (past the
        // on-marker ring, within one move of stepping in). What it buys: a unit whose lane to the
        // marker is jammed with friendlies still walks up and stacks behind the ball instead of
        // wandering off to a trivially reachable side goal - the crowded-game drift. Rides
        // ObjectiveDelta, so the round/posture scaling applies unchanged; on-marker (+1) always
        // dominates. On an ally-held marker the band starts where the ally-contest penalty ends,
        // so the pair reads "surround your teammate's marker, never step on it".
        public static float MoveObjectiveSupport = 0.3f;

        // ADDED 2026-07-10 after the second gate failure (mirror avg 25.4% - ledger entry): melee
        // armies collapsed because a one-step score gives units outside charge reach no reason to
        // close (offense 0 beyond 12", retaliation punishes proximity). Approach = the melee
        // exchange margin-if-reached x the fraction of the charge gap this move closes; 0.75 keeps
        // a completed approach worth less than the actual charge (MoveDamage 1.0), so real charges
        // still dominate when reachable.
        public static float MoveApproach = 0.75f;

        // ADDED 2026-07-10 (A5-3) from the a5-2-gate loss reading: ObjectiveDelta only pays ON the
        // marker, so a unit two moves out had NO reason to close - the same greedy-horizon hole as
        // the melee approach, on the other win condition. Shooter armies froze against hordes
        // (every exchange negative + retaliation punishes proximity => Hold/Pass) and conceded the
        // marker race in round 4. Approach pays a fraction of the gap closed toward the nearest
        // objective we do not already own; below MoveObjective so ARRIVING still dominates.
        public static float MoveObjectiveApproach = 0.4f;

        // --- Anti-horde play (A5-4: screening + mob breaking + round-scaled objectives) -------------
        // From the 49%-cell loss reading: elite gunlines got CAUGHT holding markers in the horde's
        // path. The Block/Escort candidates always existed (M8/M9 line walls across the threat
        // lane) - nothing credited them. Screen pays the ward's threatened value scaled by how
        // squarely the endpoint sits on the threat->ward lane; there is deliberately NO eligibility
        // gate - the retaliation term already charges each unit personally for absorbing the
        // charge, so a Tough tank or an emptied transport screens cheaply and a caster never does.
        public static float MoveScreen = 0.8f;
        // Pushing a unit below half strength is worth extra beyond the wounds: half-strength
        // morale tests rout whole mobs (the engine's own mechanic - break the horde, don't shave it).
        public static float MoraleBreakBonus = 1.3f;

        // ADDED 2026-08-05 (#359, Chris's re-report of the crowded-zone creep: a front unit with
        // no reason to advance - a long-ranged gun, say - should still step ASIDE so the ranks
        // behind can pass): value-weighted penalty for ENDING on the advance lane of a friendly
        // that has not activated yet this round (LaneGeometry; the M13 SideStep candidates exist
        // for exactly this pick). LaneGeometry scales the block by how much of the friendly's
        // move it cuts off - (1 - t) along the lane, so an ADVANCE that ends deep downrange is
        // nearly free (Chris's correction: the friendly walks into the vacated ground) - which
        // means standing MID-lane prices at about half this constant; 0.2 keeps that mid-lane
        // stand at the ~0.1 the term was sized for, comfortably above the 0.05 reachable
        // tie-break and an order below the real reasons to stand - a held marker (~0.5 round 1),
        // a paying screen (0.8 x threatened value). An Indirect unit's -1-after-moving is priced
        // by the damage term in the same currency, so "is the shuffle worth it?" resolves in the
        // argmax, not here.
        public static float MoveLaneBlock = 0.2f;

        // A5-8 (Chris): a landed charge also DEGRADES the target's next volley - it still shoots
        // on its own activation, but with fewer models and chargers obscuring lanes - so a charge
        // earns this bonus per expected wound of the target's ranged output (reference Q4/D4).
        // Deliberately a fraction, not full denial (Chris's correction: charging does not skip
        // the target's activation). Cheap durable chaff pays little retaliation for the exchange,
        // which is exactly the tarpit role; the charger's own forgone shooting needs no term
        // because the argmax already weighs the charge against its own Hold/shoot candidates.
        public static float ChargeTarpitPerWound = 0.04f;

        // A5-8 (Chris): Ambush arrivals aim BEHIND a vulnerable enemy unit, not at a marker -
        // "in real games they'll always pop up right behind a unit that they'll do lots of
        // damage to". Strike when the best victim is worth at least this much gross damage
        // value (a quarter of a reference-100 unit) and the exchange is net-positive after
        // retaliation; otherwise fall back to the most winnable objective. Arrivals cannot
        // seize markers the round they land anyway, so the strike costs no scoring tempo.
        public static float AmbushStrikeMinDamageValue = 0.25f;

        // --- Casting (A5) ---------------------------------------------------------------------------
        // A cast is layered (it never ends the activation), so the planner casts whenever the net
        // expected value is positive: base 4+ success chance x the summed target values, minus a
        // small opportunity cost per token burned (the attempt spends them win or lose). Non-damage
        // effects (buffs, debuffs, forced moves) price a flat fraction of the target's value - the
        // documented A5 placeholder; anticipatory buff valuation arrives with Phase C's evaluator.
        public static float CastEffectStaticFraction = 0.2f;
        public static float CastTokenValue = 0.02f;
        // Assist (#103): one token shifts the caster's 4+ one face = 1/6 of the spell's value,
        // boosting a friend or denying an enemy alike (the solo bot always declines). Spend while
        // that beats the token cost, capped per request so one cast never drains a whole pool.
        public const int CastAssistMaxTokens = 2;

        // --- Target choice (A4-3) -------------------------------------------------------------------
        // Shooting/melee targets score by value-weighted damage; finishing a unit off is worth extra
        // (a dead unit stops acting; a wounded one does not).
        public static float ShootingKillBonus = 1.5f;
        public static float MeleeKillBonus = 1.5f;

        // --- Transports + snipers (A5-6, Chris's review pass) ---------------------------------------
        // Cargo bails when the boat could lose this fraction of its remaining wounds to one enemy
        // activation - a transport destroyed with a unit inside spills it out Shaken.
        public static float TransportEvacuationFraction = 0.5f;
        // Takedown/single-model spell picks: prefer the model whose removal hurts - weapon output,
        // plus a bonus for models carrying their own rules (a joined hero's rules live on its MODEL
        // after the #006 merge, so this is the hero-sniping signal).
        public static float SnipeSpecialModelBonus = 1.5f;
        // Shooting prefers targets that can charge US next activation - kill the thing about to
        // eat you before the thing that cannot reach you.
        public static float ShootThreatFactor = 1.25f;
    }
}
