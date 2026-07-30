using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// Per-activation decision state shared by the Tactician's resolvers (#191 A4-2). The engine
    /// asks for the ACTION first (Choose Action) and the movement AFTER (DefineMovementPathRequest),
    /// but a good decision is the (action x macro-action) PAIR - so the action resolver plans once,
    /// caches the winning candidate here, and the movement resolver plays it out. One planner per
    /// controller; the engine thread runs a player's requests sequentially, so no locking.
    /// </summary>
    public sealed class TacticianPlanner : IMovePlanSource
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        // Analysis sink (#191 tooling): when set, every Choose Action plan is narrated - the full
        // scored candidate table, not just the winner - so headless games are replayable decisions,
        // not just outcomes. Null in normal play; costs nothing when unset.
        private readonly Action<string>? _decisionLog;

        // Belt-and-braces livelock guard on casting: every attempt burns tokens (win or lose), so
        // this can never bind in a legal game (the pool caps at 6) - it only bounds a broken loop.
        private const int MaxCastAttemptsPerActivation = 8;

        private DataBinding<UnitData>? _activeUnit;
        private MacroAction? _plan;
        private int _castAttempts;
        // Melee exchange margin + charge reach per enemy; both are endpoint-independent, so one
        // computation serves every candidate of the activation.
        private readonly Dictionary<DataReference, (float Margin, float Reach)> _meleeApproach = new();
        // A5-4 screening pair for this activation (most valuable OTHER friendly + the melee threat
        // nearest it, with the ward's threatened value) - endpoint-independent, computed lazily.
        private (Position ThreatPos, Position WardPos, float WardThreatValue)? _screenLane;
        private bool _screenLaneComputed;
        // Whether any enemy can still reach the marker before game end, per objective (keyed by
        // marker position) - endpoint-independent, computed lazily per activation.
        private readonly Dictionary<(float X, float Z), bool> _markerContestable = new();
        // Each enemy's best alternative target among our OTHER friendlies (value-weighted damage
        // at their current positions) - the one-ply reply the retaliation term prices against.
        // Endpoint-independent, computed lazily per activation.
        private readonly Dictionary<DataReference, float> _enemyAltTarget = new();
        // Where each enemy will plausibly stand after its next activation (one rush toward its
        // nearest attractive goal) + its longest effective weapon range against us - both feed
        // the arriving-pressure term. Endpoint-independent, computed lazily per activation.
        private readonly Dictionary<DataReference, Position> _enemyProjected = new();
        private readonly Dictionary<DataReference, float> _enemyMaxRange = new();
        // Round-scaled projected-objective deficit in [-1, 1] (positive = losing the race) -
        // the risk-posture tilt. Endpoint-independent, computed lazily per activation.
        private float? _posture;
        // #264 issue 1: one WALKING route per objective, from this activation's start - the gradient
        // measures against it instead of straight-line distance. Both are activation-scoped: the
        // routes start at the unit's current centroid, and the grid only depends on the terrain and
        // the unit's base size. One A* per objective, one grid build, per activation.
        private readonly Dictionary<(float X, float Z), List<Position>> _objectiveRoutes = new();
        // The same, per ENEMY, for the melee approach gradient (issue 1's melee half). Keyed by the
        // enemy rather than its centroid because that centroid is fixed for the activation anyway.
        private readonly Dictionary<DataReference, List<Position>> _enemyRoutes = new();
        private TerrainGrid? _routeGrid;
        // Strider / Flying, read once per activation (see UnitRoute).
        private bool? _ignoresAllTerrain;
        private bool? _ignoresDifficultTerrain;

        /// <summary>The unit whose activation is being planned (null between activations).</summary>
        public DataBinding<UnitData>? ActiveUnit => _activeUnit;

        public TacticianPlanner(ITableState tableState, RuleEvaluator evaluator,
            Action<string>? decisionLog = null)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _decisionLog = decisionLog;
        }

        /// <summary>Called by the activation resolver the moment it picks the unit.</summary>
        public void BeginActivation(DataBinding<UnitData> unit)
        {
            _activeUnit = unit;
            _plan = null;
            _castAttempts = 0;
            _meleeApproach.Clear();
            _screenLane = null;
            _screenLaneComputed = false;
            _markerContestable.Clear();
            _enemyAltTarget.Clear();
            _enemyProjected.Clear();
            _enemyMaxRange.Clear();
            _posture = null;
            _objectiveRoutes.Clear();
            _enemyRoutes.Clear();
            _routeGrid = null;
            _ignoresAllTerrain = null;
            _ignoresDifficultTerrain = null;
        }

        /// <summary>
        /// Plans this activation and answers Choose Action. Returns null when the planner has no
        /// claim (no known active unit, or its planned choice is not among the valid options) -
        /// the caller then falls back to solo behavior, never faulting the stage (G3).
        /// </summary>
        public string? ChooseAction(IReadOnlyList<string> validOptions)
        {
            if (_activeUnit == null) return null;

            // A5-5: an embarked unit's only real choice is Disembark-or-ride. The macro generator
            // has no candidates for an off-table unit, so without this the fallback chain ended in
            // Pass and cargo RODE UNTIL THE TRANSPORT DIED (zero voluntary disembarks in the
            // DE-vs-Orks logs - the whole payload never fought). Get out when the transport has
            // arrived somewhere worth fighting for; keep riding otherwise.
            if (validOptions.Contains(CoreRuleCatalog.DisembarkRuleName) && WantsDisembark())
                return CoreRuleCatalog.DisembarkRuleName;

            // A5: casting is layered - it loops straight back here without ending the activation -
            // so a positive-value cast is taken FIRST whenever the engine offers Cast; the planned
            // action still happens on re-entry. Checked before the post-move branch too, which is
            // what makes an M11 MoveToCast set-up move pay off in the same activation.
            if (validOptions.Contains(ChooseActionStage.CAST_CHOICE_NAME)
                && _castAttempts < MaxCastAttemptsPerActivation
                && BestAffordableCast() != null)
            {
                _castAttempts++;
                return ChooseActionStage.CAST_CHOICE_NAME;
            }

            // Post-move re-entry: the plan was played out; shoot if the engine still lets us,
            // otherwise end the activation.
            if (_plan != null)
            {
                if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME))
                    return ChooseActionStage.SHOOT_CHOICE_NAME;
                if (validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME))
                    return ChooseActionStage.PASS_CHOICE_NAME;
                return null;
            }

            List<MacroAction> candidates = MacroActionGenerator.Enumerate(_evaluator, _tableState, _activeUnit);
            MacroAction? best = null;
            string? bestAction = null;
            float bestScore = float.NegativeInfinity;
            List<(MacroAction Candidate, string Action, float Score)>? scored =
                _decisionLog == null ? null : new();

            foreach (MacroAction candidate in candidates)
            {
                string? action = ActionNameFor(candidate, validOptions);
                if (action == null) continue; // not executable under the currently valid actions

                float score = Score(candidate);
                scored?.Add((candidate, action, score));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestAction = action;
                }
            }

            if (scored != null) LogDecision(scored, best, bestAction);
            LogIfStuck(candidates);
            if (best == null || bestAction == null) return null;

            // Hold-and-shoot / pass need no movement; movement plans are cached for the move request.
            if (bestAction == ChooseActionStage.MOVEMENT_CHOICE_NAME
                || bestAction == ChooseActionStage.CHARGE_CHOICE_NAME)
            {
                _plan = best;
            }
            else
            {
                _plan = best; // mark the activation as decided so re-entry shoots/passes
            }

            return bestAction;
        }

        // #256's stuck detector, filed for #264: when EVERY movement candidate the generator can
        // build nets under an inch, the unit is WEDGED - a walled pocket, a corner funnel, a friendly
        // seal - and the symptom on the table is just a unit that looks passive. Say so, so a game
        // reports this failure class instead of leaving it to be reconstructed from a screenshot.
        private const float StuckMoveInches = 1f;

        private void LogIfStuck(List<MacroAction> candidates)
        {
            if (_decisionLog == null || _activeUnit == null) return;

            UnitData self = _activeUnit.GetValue();
            Position at = Centroid(self);
            bool anyRealMove = candidates.Any(c => c.Intent != EMacroIntent.Hold
                && Distance(at, c.ProjectedCentroid) >= StuckMoveInches);
            if (anyRealMove) return;

            _decisionLog($"stuck {self.Name} at ({at.x:F1},{at.z:F1}): every movement candidate " +
                $"nets < {StuckMoveInches:F0} inch");
        }

        // One block per Choose Action: the winner line, then the full candidate table (score-desc)
        // in the same format the FdgLab analyze command prints, so logs and save dumps read alike.
        private void LogDecision(List<(MacroAction Candidate, string Action, float Score)> scored,
            MacroAction? best, string? bestAction)
        {
            if (_decisionLog == null || _activeUnit == null) return;
            UnitData self = _activeUnit.GetValue();
            Position at = Centroid(self);
            _decisionLog($"plan {self.Name} at ({at.x:F1},{at.z:F1}) -> " +
                (best == null ? "(no claim: solo fallback)" : $"{bestAction} [{best.Intent}]"));
            foreach ((MacroAction k, string action, float score) in scored.OrderByDescending(s => s.Score))
            {
                string target = k.TargetEnemy != null ? $" vs {k.TargetEnemy.Name}"
                    : k.TargetObjective != null
                        ? $" obj({k.TargetObjective.Position.x:F0},{k.TargetObjective.Position.z:F0})"
                        : k.TargetAlly != null ? $" ally {k.TargetAlly.Name}" : "";
                _decisionLog($"  {score,8:F4}  {action,-6} {k.Intent,-18} {k.Feasibility,-13} " +
                    $"end=({k.ProjectedCentroid.x:F1},{k.ProjectedCentroid.z:F1}){target}");
            }
        }

        /// <summary>
        /// The cached move for this unit's DefineMovementPathRequest, or null (fall back to solo).
        /// Cleared on hand-out - each activation moves at most once.
        /// </summary>
        public List<ModelMoveEntry>? TakePlannedMove(DataBinding<UnitData> unit)
        {
            if (_activeUnit == null || _plan == null) return null;
            if (!ReferenceEquals(unit, _activeUnit) && !unit.Reference.Equals(_activeUnit.Reference)) return null;
            if (_plan.Intent == EMacroIntent.Hold) return null;

            List<ModelMoveEntry> move = _plan.Move;
            return move.Count == 0 ? null : move;
        }

        // --- casting (A5) ---------------------------------------------------------------------------

        /// <summary>
        /// Answers the cast stage's spell picker: the highest-net-value OFFERED spell, never
        /// Cancel - a cancelled pick loops straight back to Choose Action with nothing spent, so
        /// under a deterministic policy it must be unreachable (the forced-Cast livelock class).
        /// Null when the caster or its army is unknown; the solo fallback then picks the first
        /// spell, which also spends tokens and terminates.
        /// </summary>
        public string? ChooseSpell(IReadOnlyList<string> validOptions)
        {
            if (_activeUnit == null) return null;
            ArmyData? army = SpellValuation.ArmyOf(_tableState, _activeUnit.GetValue().PlayerID);
            if (army == null) return null;

            string? bestLabel = null;
            float bestValue = float.NegativeInfinity;
            foreach (string option in validOptions)
            {
                RuntimeSpell? spell = SpellValuation.FindByLabel(army, option);
                if (spell == null) continue; // Cancel, or a label we cannot map back to a spell
                float value = SpellValuation.NetCastValue(_evaluator, _tableState, _activeUnit, spell);
                if (bestLabel == null || value > bestValue)
                {
                    bestValue = value;
                    bestLabel = option;
                }
            }
            return bestLabel;
        }

        /// <summary>
        /// Answers one "Choose target for {spell} ({k} of up to {N})" pick with the highest-value
        /// option. A null <paramref name="choice"/> with a true return is a deliberate cancel -
        /// only ever once the spell's minimum target count is met, because cancelling earlier
        /// aborts the whole cast UNSPENT and Choose Action would re-plan the same cast forever.
        /// False = no claim (unparseable instructions, unknown spell); caller falls back to solo.
        /// </summary>
        public bool TryChooseSpellTarget(string instructions,
            IReadOnlyList<SelectionRequest<UnitData>.ValidOption> options,
            out DataBinding<UnitData>? choice)
        {
            choice = null;
            if (_activeUnit == null || options.Count == 0) return false;

            const string prefix = "Choose target for ";
            if (!instructions.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int suffixStart = instructions.LastIndexOf(" (", StringComparison.Ordinal);
            if (suffixStart <= prefix.Length) return false;
            string spellName = instructions[prefix.Length..suffixStart];
            string suffix = instructions[(suffixStart + 2)..];
            int ofIndex = suffix.IndexOf(" of up to ", StringComparison.Ordinal);
            if (ofIndex < 0 || !int.TryParse(suffix[..ofIndex], out int pickIndex)) return false;

            ArmyData? army = SpellValuation.ArmyOf(_tableState, _activeUnit.GetValue().PlayerID);
            RuntimeSpell? spell = army == null ? null : SpellValuation.FindByName(army, spellName);
            if (spell == null) return false;

            PlayerID us = _activeUnit.GetValue().PlayerID;
            DataBinding<UnitData>? best = null;
            float bestValue = float.NegativeInfinity;
            foreach (SelectionRequest<UnitData>.ValidOption option in options)
            {
                bool friendly = SpellValuation.IsFriendlyTo(
                    _tableState, us, option.Option.GetValue().PlayerID);
                float value = SpellValuation.TargetValue(
                    _evaluator, _activeUnit, spell, option.Option, friendly);
                if (best == null || value > bestValue)
                {
                    bestValue = value;
                    best = option.Option;
                }
            }

            // Extra targets only while they ADD value; stopping is only legal past the minimum.
            if (pickIndex > Math.Max(1, spell.Target.MinCount) && bestValue <= 0f) return true;
            choice = best;
            return true;
        }

        /// <summary>
        /// #197 Surprise Attack: which enemy the first-activation hit pool should fall on. Scored the
        /// same way <see cref="SpellValuation.TargetValue"/> scores a damage spell - the FRACTION of the
        /// target's remaining wounds the burst is expected to remove, weighted by what the target is
        /// worth - so a burst that wipes a small elite beats one that scratches a big cheap block.
        ///
        /// <para>Deliberately simple: no positional or objective context, no "save it for later" (the
        /// burst is mandatory and this is the only activation it can happen on). The pool's config is
        /// read off the ACTIVE unit's own rule, since a burst can only be resolving during its bearer's
        /// activation; false (fall back to the solo bot's first option) if it cannot be found.</para>
        /// </summary>
        public bool TryChooseBurstTarget(string instructions,
            IReadOnlyList<SelectionRequest<UnitData>.ValidOption> options,
            out DataBinding<UnitData>? choice)
        {
            choice = null;
            if (_activeUnit == null || options.Count == 0) return false;
            if (!instructions.StartsWith(SurpriseAttackStage.PICK_INSTRUCTION_PREFIX, StringComparison.Ordinal))
                return false;

            string ruleName = instructions[SurpriseAttackStage.PICK_INSTRUCTION_PREFIX.Length..];
            if (!TryFindPool(ruleName, out Effect.DealPooledHits? pool, out int diceCount)) return false;

            DataBinding<UnitData>? best = null;
            float bestValue = float.NegativeInfinity;
            foreach (SelectionRequest<UnitData>.ValidOption option in options)
            {
                UnitData candidate = option.Option.GetValue();
                AttackEstimate estimate = CombatMath.EstimatePooledHits(_evaluator, _activeUnit,
                    option.Option, ruleName, diceCount, pool!.SuccessThreshold, pool.ArmorPenetration);

                float fraction = Math.Min(1f, estimate.ExpectedWounds / Math.Max(1f, candidate.RemainingWounds));
                float value = fraction * TacticalAnalysis.UnitValue(candidate) / 100f;
                if (best == null || value > bestValue)
                {
                    bestValue = value;
                    best = option.Option;
                }
            }

            choice = best;
            return true;
        }

        /// <summary>
        /// The active unit's pooled-hit ability, with its dice count resolved against the bearing rule's
        /// arguments (Surprise Attack(5) -> 5). Matches the rule the prompt names; falls back to the only
        /// pooled-hit rule the unit carries when the name does not match, so a display-name change cannot
        /// silently downgrade the AI to first-option picking. Scans models too - a joined hero keeps its
        /// rules on its own model (#093).
        /// </summary>
        private bool TryFindPool(string ruleName, out Effect.DealPooledHits? pool, out int diceCount)
        {
            pool = null;
            diceCount = 0;
            if (_activeUnit == null) return false;

            IUnit bearer = _activeUnit.GetValue();
            var found = new List<(ResolvedRule Rule, Effect.DealPooledHits Pool)>();

            foreach (ResolvedRule rule in bearer.RuleDefinitions
                         .Concat(bearer.Models.Where(model => model.GetIsAlive())
                             .SelectMany(model => model.RuleDefinitions)))
            {
                foreach (ActivatedAbility ability in rule.Definition.Activated)
                {
                    if (ability.Effect is Effect.DealPooledHits pooled)
                    {
                        found.Add((rule, pooled));
                    }
                }
            }

            if (found.Count == 0) return false;

            (ResolvedRule Rule, Effect.DealPooledHits Pool) match = found
                .FirstOrDefault(entry => string.Equals(entry.Rule.RequestedName, ruleName,
                    StringComparison.OrdinalIgnoreCase));
            if (match.Pool == null)
            {
                if (found.Count > 1) return false;
                match = found[0];
            }

            pool = match.Pool;
            diceCount = match.Pool.DiceCount.Resolve(new RuleInvocation(Hook: null, bearer,
                match.Rule.Arguments, Definition: match.Rule.Definition));
            return diceCount > 0;
        }

        // A5-5 disembark timing: the transport has ARRIVED when a marker we do not own outright,
        // or an enemy the cargo would beat in melee, is within the cargo's reach after the 6"
        // placement. Otherwise keep riding (M12 DeliverCargo is still driving the boat there).
        private bool WantsDisembark()
        {
            UnitData self = _activeUnit!.GetValue();
            DataBinding<UnitData>? transport = null;
            if (Rules.Dispatch.TransportUtilities.GetTransportId(self) is UnitID transportId)
            {
                foreach (DataBinding<UnitData> friendly in FriendlyBindings(self.PlayerID))
                    if (friendly.GetValue().ID == transportId) { transport = friendly; break; }
            }
            if (transport == null) return true; // no live transport to ride: get out
            Position here = Centroid(transport.GetValue());

            // A5-6 (Chris): if the boat could lose half its remaining wounds to a single enemy
            // activation, bail NOW - a transport destroyed with cargo inside spills it out Shaken.
            float incoming = 0f;
            foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(self.PlayerID))
            {
                UnitData enemy = enemyBinding.GetValue();
                float d = Distance(here, Centroid(enemy));
                float theirReach = Math.Max(1f, d - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                incoming = Math.Max(incoming, CombatMath.EstimateShooting(_evaluator, enemyBinding,
                    transport, new AttackContext(theirReach, AttackerMoved: true)).ExpectedWounds);
                if (enemy.GetMeleeWeapons().Count > 0
                    && TacticalAnalysis.MeleeThreatReach(enemy, transport.GetValue(), _evaluator) >= d - 1f)
                {
                    incoming = Math.Max(incoming, CombatMath.EstimateMelee(_evaluator, enemyBinding,
                        transport).AttackerAttack.ExpectedWounds);
                }
            }
            if (incoming >= TacticianWeights.TransportEvacuationFraction
                    * transport.GetValue().RemainingWounds)
                return true;

            const float placementInches = 6f;
            float cargoReach = placementInches + TacticalAnalysis.AdvanceDistance(self, _evaluator);

            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool ours = TacticalAnalysis.IsProjectedOwnerAllied(
                    _tableState, projection, self.PlayerID); // #296: team-owned = ours
                if (!ours && Distance(here, projection.Objective.Position)
                        <= cargoReach + TacticalAnalysis.ObjectiveSeizureRadiusInches)
                    return true;
            }

            if (self.GetMeleeWeapons().Count > 0)
            {
                foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(self.PlayerID))
                {
                    (float margin, float reach) = MeleeApproachAgainst(enemyBinding);
                    if (margin <= 0f) continue;
                    if (Distance(here, Centroid(enemyBinding.GetValue())) <= placementInches + reach)
                        return true;
                }
            }
            return false;
        }

        // The best strictly-positive-value spell the unit can afford right now, or null. The
        // engine gates the Cast option on ITS eligibility (affordable + legal target), so this
        // only decides whether casting is WORTH it.
        private RuntimeSpell? BestAffordableCast()
        {
            UnitData self = _activeUnit!.GetValue();
            ArmyData? army = SpellValuation.ArmyOf(_tableState, self.PlayerID);
            if (army == null) return null;

            int tokens = SpellPurse.Available(_tableState, _evaluator, self);
            RuntimeSpell? best = null;
            float bestValue = 0f; // strictly positive expected value required to bother
            foreach (RuntimeSpell spell in army.Spells)
            {
                if (spell.Threshold <= 0 || spell.Threshold > tokens) continue;
                float value = SpellValuation.NetCastValue(_evaluator, _tableState, _activeUnit!, spell);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = spell;
                }
            }
            return best;
        }

        // --- scoring --------------------------------------------------------------------------------

        /// <summary>
        /// The A4-2 greedy score (plan sec. 8): value-weighted damage dealt from the candidate's
        /// endpoint, minus expected retaliation onto that endpoint, plus the objective-control delta.
        /// Weights live in TacticianWeights; tuning is benchmark-driven and recorded.
        /// </summary>
        public float Score(MacroAction candidate)
        {
            UnitData self = _activeUnit!.GetValue();
            Position end = candidate.ProjectedCentroid;
            Position now = Centroid(self);
            bool meleeCapable = self.GetMeleeWeapons().Count > 0;

            float offense = 0f;
            float retaliation = 0f;
            float approach = 0f;
            float projectedThreat = 0f;
            foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(self.PlayerID))
            {
                UnitData enemy = enemyBinding.GetValue();
                Position enemyPos = Centroid(enemy);
                float endDistance = Distance(end, enemyPos);

                if (candidate.Intent == EMacroIntent.ChargeToContact
                    && candidate.ActionType == EActionType.Charge
                    && candidate.TargetEnemy != null && ReferenceEquals(candidate.TargetEnemy, enemy)
                    && candidate.Feasibility == EFeasibility.Reachable)
                {
                    MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, _activeUnit, enemyBinding);
                    // A5-8 (Chris): a landed charge also degrades the target's next volley (it
                    // still shoots on its own activation - with fewer guns and chargers in the
                    // way), so tying up a shooter earns a tarpit bonus on top of the exchange.
                    offense = Math.Max(offense,
                        ValueFraction(melee.AttackerAttack.ExpectedWounds, enemy)
                        - ValueFraction(melee.DefenderReturn.ExpectedWounds, self)
                        + TacticianWeights.ChargeTarpitPerWound * TacticalAnalysis.RangedOutputWounds(enemy));
                }
                else if (CanShootAfter(candidate))
                {
                    AttackEstimate shot = CombatMath.EstimateShooting(_evaluator, _activeUnit, enemyBinding,
                        new AttackContext(Math.Max(1f, endDistance), AttackerMoved: candidate.Intent != EMacroIntent.Hold));
                    offense = Math.Max(offense, ValueFraction(shot.ExpectedWounds, enemy));
                }

                // Approach value (#191 A4 gate fix - the mechanism behind the two failed gates:
                // greedy one-step scoring gave melee units outside charge reach no reason to close).
                // Closing toward a PROFITABLE charge is worth part of the exchange margin we'd get
                // on arrival, scaled by how much of the remaining charge gap this move closes.
                // Zero once the enemy is already in reach - the real charge candidate scores there.
                if (meleeCapable)
                {
                    (float margin, float reach) = MeleeApproachAgainst(enemyBinding);
                    // #264 issue 1, the MELEE half (slice 1 fixed the objective half and deferred
                    // this one): WALKING distance, not straight-line. Behind a large impassible
                    // piece the two diverge by the whole detour, and a correct first leg can
                    // LENGTHEN the straight-line gap - so the single term that pays a melee unit
                    // for crossing the table paid zero exactly when the crossing was hardest, and
                    // retreat/hold won the argmax by default.
                    (List<Position> route, List<ITerrain> routeTerrain, float routeRadius) =
                        RouteToEnemy(now, enemyBinding, enemyPos);
                    float routeLength = RouteMetrics.Length(route);
                    // Only a real DETOUR switches the measure. With a clear lane the route IS the
                    // straight segment, and there straight-line distance is the EXACT answer while
                    // RemainingFrom's project-onto-the-route approximation is not - it charges
                    // lateral displacement as distance not closed, so a flanking step reads as a
                    // wasted one. (RemainingFrom exists to avoid an A* per candidate; it is worth
                    // its error only where the detour it approximates actually exists.) Gating here
                    // keeps this slice strictly additive: scoring is untouched wherever no
                    // impassible piece stands between the unit and its target - measured, not
                    // assumed, by the bit-identical no-terrain benchmark in the ledger.
                    bool detours = routeLength > Distance(now, enemyPos) + 0.01f;
                    float gapNow = (detours ? routeLength : Distance(now, enemyPos)) - reach;
                    if (margin > 0f && gapNow > 1f)
                    {
                        float gapEnd = Math.Max(0f, (detours
                            ? RouteMetrics.RemainingFrom(route, end, routeTerrain, routeRadius)
                            : endDistance) - reach);
                        // A5-6 (Chris): charging beats being charged. No credit for walking INSIDE
                        // the enemy's own threat reach (charge budget + the 2" melee cylinder,
                        // +1.5" centroid slack) - the approach stages at their line and takes the
                        // charge on OUR next activation instead of eating theirs.
                        // The stage gap stays a STRAIGHT-line quantity: it models weapon threat,
                        // which does not walk around walls. Clamping a route gap from below with it
                        // is conservative in the right direction - route distance is never shorter
                        // than straight-line, so the floor can only hold the approach back.
                        if (enemy.GetMeleeWeapons().Count > 0)
                        {
                            float stageGap = Math.Max(0f,
                                TacticalAnalysis.MeleeThreatReach(enemy, self, _evaluator) + 1.5f - reach);
                            gapEnd = Math.Max(gapEnd, Math.Min(stageGap, gapNow));
                        }
                        approach = Math.Max(approach,
                            margin * Math.Clamp((gapNow - gapEnd) / gapNow, 0f, 1f));
                    }
                }

                // What the enemy could do to us at the endpoint next activation (their advance included).
                float theirReach = Math.Max(1f, endDistance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                AttackEstimate incoming = CombatMath.EstimateShooting(_evaluator, enemyBinding, _activeUnit,
                    new AttackContext(theirReach, AttackerMoved: true));
                float incomingValue = ValueFraction(incoming.ExpectedWounds, self);
                // Melee threat: if they can charge the endpoint (charge + 2" melee cylinder),
                // count their melee margin too.
                if (TacticalAnalysis.MeleeThreatReach(enemy, self, _evaluator) >= endDistance - 1f
                    && enemy.GetMeleeWeapons().Count > 0)
                {
                    incomingValue = Math.Max(incomingValue, 0.5f * ValueFraction(
                        CombatMath.EstimateMelee(_evaluator, enemyBinding, _activeUnit).AttackerAttack.ExpectedWounds, self));
                }
                // One-ply reply (#191, replaces the 2026-07-11 per-sharer dilution): this enemy's
                // volley lands on ONE target per activation, and it picks that target
                // adversarially - so what we pay is our share of its best reply, not a headcount
                // discount. ours/(ours + best-alternative) generalizes the old divisor: alone we
                // pay full price, behind a fatter target we pay near the floor, and a juicy unit
                // can no longer hide behind chaff that a count-based dilution priced the same.
                if (incomingValue > 0f)
                {
                    float alt = BestAlternativeTargetValue(enemyBinding);
                    float share = Math.Max(TacticianWeights.RetaliationShareFloor,
                        incomingValue / (incomingValue + alt));
                    retaliation = Math.Max(retaliation, incomingValue * share);
                }
                // Arriving pressure (#191 idea 2): an enemy too far to answer THIS round prices
                // nothing above, but one projected rush can put the endpoint inside its threat
                // NEXT round. Only enemies the current term ignored are priced (no double count),
                // at the low MoveProjectedThreat weight - a forecast, not a threat in being.
                // Projected melee from an enemy we would happily charge (positive margin) is an
                // opportunity, not a threat - the staged charge must not be penalized for
                // standing its ground.
                else
                {
                    Position projected = ProjectedEnemyPosition(enemyBinding);
                    float projDistance = Distance(end, projected);
                    float projReach = Math.Max(1f,
                        projDistance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                    float projValue = projReach > EnemyMaxRangeAgainstUs(enemyBinding) ? 0f
                        : ValueFraction(CombatMath.EstimateShooting(_evaluator, enemyBinding,
                                _activeUnit, new AttackContext(projReach, AttackerMoved: true))
                            .ExpectedWounds, self);
                    if (enemy.GetMeleeWeapons().Count > 0
                        && MeleeApproachAgainst(enemyBinding).Margin <= 0f
                        && TacticalAnalysis.MeleeThreatReach(enemy, self, _evaluator) >= projDistance - 1f)
                    {
                        projValue = Math.Max(projValue, 0.5f * ValueFraction(CombatMath.EstimateMelee(
                                _evaluator, enemyBinding, _activeUnit).AttackerAttack.ExpectedWounds,
                            self));
                    }
                    projectedThreat = Math.Max(projectedThreat, projValue);
                }
            }

            float objectiveDelta = ObjectiveDelta(self, end);
            float objectiveApproach = ObjectiveApproach(now, end);
            // A5-4: markers matter most when the round about to score is the last one; early
            // rounds, attrition buys the endgame (a marker held in a horde's path is lost later).
            float urgency = ObjectiveUrgency(_tableState.Progress.RoundCount ?? 1,
                _tableState.Progress.TotalRounds);

            // Risk posture (#191 idea 3): behind on projected markers late, risks get cheaper
            // and flips more valuable - the losing side must buy variance; ahead, retaliation
            // prices UP by the same slope and the lead is protected. The objective boost is
            // behind-only: being ahead is no reason to stop playing the markers.
            float posture = Posture();
            float riskScale = 1f - TacticianWeights.PostureRetaliationRelief * posture;
            float objectiveScale = 1f + TacticianWeights.PostureObjectiveBoost * Math.Max(0f, posture);

            float substantive =
                   TacticianWeights.MoveDamage * offense
                 - riskScale * TacticianWeights.MoveRetaliation * retaliation
                 - riskScale * TacticianWeights.MoveProjectedThreat * projectedThreat
                 + objectiveScale * urgency * TacticianWeights.MoveObjective * objectiveDelta
                 // A5-8: the gradient is deadline-scaled per objective INSIDE ObjectiveApproach.
                 + objectiveScale * TacticianWeights.MoveObjectiveApproach * objectiveApproach
                 + TacticianWeights.MoveApproach * approach
                 + TacticianWeights.MoveScreen * ScreenValue(end);

            // #264 issue 2: the reachable bonus is a TIE-BREAK between candidates that are already
            // worth something ("if two plans are worth the same, take the one that actually gets
            // there"), never a reason to do a worthless thing. Ungated it made every trivially
            // reachable goal - FallBack, Concentrate, SeekCover, Block - collect a flat 0.05 that a
            // real detour's substantive terms could not match, so it decided WHICH bad candidate
            // won and a walled unit rushed backwards to the table edge.
            bool tieBreak = candidate.Feasibility == EFeasibility.Reachable && substantive > 0f;
            return substantive + (tieBreak ? TacticianWeights.MoveReachableBonus : 0f);
        }

        /// <summary>Round scaling for the objective terms (#191 A5-4): ~0.66 in round 1 rising to
        /// 1.3 in the final round, when ownership becomes the score.</summary>
        internal static float ObjectiveUrgency(int round, int totalRounds)
        {
            float t = (float)round / Math.Max(1, totalRounds);
            return t >= 1f ? 1.3f : 0.55f + 0.45f * t;
        }

        // A5-4: credit for interposing on the lane between the biggest melee threat and our most
        // valuable OTHER unit - the M8/M9 candidates always existed, nothing paid them. There is
        // deliberately no who-may-screen gate: the retaliation term already charges each unit
        // personally for absorbing the charge, so tough cheap bodies screen and casters do not.
        private float ScreenValue(Position end)
        {
            (Position threatPos, Position wardPos, float wardThreatValue)? lane = ScreenLane();
            if (lane == null) return 0f;

            // #296: a screen stands BETWEEN the threat and the ward. DistanceToSegment clamps to the
            // endpoints, so a spot BEHIND the ward (or the threat) within 5" still measured as
            // on-lane and collected the credit - which paid a backward "screen" that screens nothing
            // (the crowded-2v2 round-1 FallBack winner). Interior of the segment only.
            float abx = lane.Value.wardPos.x - lane.Value.threatPos.x;
            float abz = lane.Value.wardPos.z - lane.Value.threatPos.z;
            float lengthSq = abx * abx + abz * abz;
            if (lengthSq <= 0.0001f) return 0f;
            float t = ((end.x - lane.Value.threatPos.x) * abx
                     + (end.z - lane.Value.threatPos.z) * abz) / lengthSq;
            if (t <= 0.05f || t >= 0.95f) return 0f;

            float toLane = DistanceToSegment(end, lane.Value.threatPos, lane.Value.wardPos);
            // Squarely on the lane counts fully; credit fades to nothing 5" off it.
            float intercept = Math.Clamp((5f - toLane) / (5f - 1.5f), 0f, 1f);
            return intercept * lane.Value.wardThreatValue;
        }

        private (Position, Position, float)? ScreenLane()
        {
            if (_screenLaneComputed) return _screenLane;
            _screenLaneComputed = true;

            UnitData self = _activeUnit!.GetValue();
            // A5-8 (Chris): pick the ward by THREATENED value, not raw value - of everything in
            // the army, a D2/Tough(18) monolith needs protection the least, and while it topped
            // the raw-value pick its ~zero exchange margin nulled the lane so NOBODY screened
            // ANYONE. Ward = the friendly that stands to LOSE the most value to the melee threat
            // nearest it (A5-4b margin, so a counter-blade powerhouse still needs no screen).
            List<DataBinding<UnitData>> meleeEnemies = EnemyBindings(self.PlayerID)
                .Where(e => e.GetValue().GetMeleeWeapons().Count > 0).ToList();
            DataBinding<UnitData>? ward = null;
            DataBinding<UnitData>? threat = null;
            float wardThreatValue = 0f;
            foreach (DataBinding<UnitData> friendly in FriendlyBindings(self.PlayerID))
            {
                if (friendly.Reference.Equals(_activeUnit.Reference)) continue;

                Position friendlyPos = Centroid(friendly.GetValue());
                DataBinding<UnitData>? nearest = null;
                float nearestDistance = float.MaxValue;
                foreach (DataBinding<UnitData> enemyBinding in meleeEnemies)
                {
                    float d = Distance(Centroid(enemyBinding.GetValue()), friendlyPos);
                    if (d < nearestDistance) { nearestDistance = d; nearest = enemyBinding; }
                }
                // A screen only matters while the threat could realistically reach the ward soon
                // (their move + charge next activation, with a little slack).
                if (nearest == null || nearestDistance > 26f) continue;

                MeleeEstimate exchange = CombatMath.EstimateMelee(_evaluator, nearest, friendly);
                float inbound = ValueFraction(exchange.AttackerAttack.ExpectedWounds, friendly.GetValue());
                // A5-6: a loaded transport is worth boat + payload - protect the delivery.
                float plainValue = TacticalAnalysis.UnitValue(friendly.GetValue());
                if (plainValue > 0f)
                    inbound *= TacticalAnalysis.UnitValueWithCargo(friendly.GetValue(), _tableState, _evaluator) / plainValue;
                float margin = inbound
                    - ValueFraction(exchange.DefenderReturn.ExpectedWounds, nearest.GetValue());
                if (margin <= wardThreatValue) continue;
                wardThreatValue = margin;
                ward = friendly;
                threat = nearest;
            }
            if (ward == null || threat == null) return null;
            Position wardPos = Centroid(ward.GetValue());

            // A5-4b: one screen per lane - if another friendly already stands on it, the lane is
            // manned and a second body adds nothing (no dogpiled screens).
            Position threatPos = Centroid(threat.GetValue());
            foreach (DataBinding<UnitData> friendly in FriendlyBindings(self.PlayerID))
            {
                if (friendly.Reference.Equals(_activeUnit!.Reference)
                    || friendly.Reference.Equals(ward.Reference)) continue;
                if (DistanceToSegment(Centroid(friendly.GetValue()), threatPos, wardPos) <= 2.5f)
                    return null;
            }

            _screenLane = (threatPos, wardPos, wardThreatValue);
            return _screenLane;
        }

        private static float DistanceToSegment(Position p, Position a, Position b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float lengthSq = abx * abx + abz * abz;
            if (lengthSq <= 0.0001f) return Distance(p, a);
            float t = Math.Clamp(((p.x - a.x) * abx + (p.z - a.z) * abz) / lengthSq, 0f, 1f);
            return Distance(p, new Position(a.x + t * abx, a.z + t * abz));
        }

        // #296: FRIENDS are the whole team, not just this player's own army - a teammate's units
        // are wards to screen, alternative targets the enemy might prefer, and mass to concentrate on.
        private IEnumerable<DataBinding<UnitData>> FriendlyBindings(PlayerID us)
        {
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (!TacticalAnalysis.AreAllied(_tableState, us, army.PlayerID)
                    || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().Models.Any(m => m.GetIsAlive())
                        && unit.GetValue().GetIsOnBattlefield())
                        yield return unit;
            }
        }

        // Past this much negative slack the marker cannot be reached even rushing every remaining
        // round - no credit, no futile marches. A little negative is still paid (worth trying).
        private const float HopelessSlackRounds = -1f;
        // How fast the deadline urgency decays back to the round baseline per round of slack.
        private const float DeadlineDecayPerSlackRound = 0.3f;

        // A5-3: the gradient HALF of the objective term - the fraction of the gap to the nearest
        // not-ours objective this move closes. ObjectiveDelta pays only ON the marker; without a
        // gradient a unit two moves out never starts walking (the shooter-freeze mechanism from
        // the a5-2-gate loss reading, the melee-approach bug's twin).
        // A5-8 (Chris): deadline-aware per objective - a Slow army must leave EARLY, a fast one
        // can shoot now and pop on late. Urgency is the full final-round 1.3 when the unit must
        // start now to arrive by game end (slack ~0), decays to the round baseline as slack
        // grows (a shooter with time to spare keeps shooting), and is zero when the marker is
        // unreachable even rushing every remaining round.
        private float ObjectiveApproach(Position now, Position end)
        {
            float onIt = TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f;
            UnitData self = _activeUnit!.GetValue();
            float speed = Math.Max(1f, TacticalAnalysis.RushDistance(self, _evaluator));
            int round = _tableState.Progress.RoundCount ?? 1;
            int totalRounds = _tableState.Progress.TotalRounds;
            float movesLeft = totalRounds - round + 1;
            float baseline = ObjectiveUrgency(round, totalRounds);

            float best = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                // #296: an ALLY-held marker is not a gradient target - it is already the side's
                // marker (#297 team-aware reconcile), exactly like one we hold ourselves.
                if (TacticalAnalysis.IsProjectedOwnerAllied(_tableState, projection, self.PlayerID))
                    continue;

                // #264 issue 1: WALKING distance, not straight-line - behind a large impassible
                // piece the two diverge by the whole detour, and the straight-line version paid a
                // correct first leg ~nothing (or charged it, when rounding the wall transiently
                // grows the Euclidean gap). It also makes the deadline slack honest: a marker 17"
                // away down a 30" route is one move further out than it looks.
                (List<Position> route, List<ITerrain> terrain, float baseRadius) =
                    RouteToObjective(now, projection.Objective.Position);
                float gapNow = RouteMetrics.Length(route) - onIt;
                if (gapNow <= 0f) continue; // already there: ObjectiveDelta pays, not the gradient
                float slack = movesLeft - gapNow / speed;
                if (slack < HopelessSlackRounds) continue;
                float urgency = Math.Clamp(1.3f - DeadlineDecayPerSlackRound * (slack - 0.25f),
                    baseline, 1.3f);

                float gapEnd = Math.Max(0f,
                    RouteMetrics.RemainingFrom(route, end, terrain, baseRadius) - onIt);
                best = Math.Max(best, urgency * Math.Clamp((gapNow - gapEnd) / gapNow, 0f, 1f));
            }
            return best;
        }

        // The route this unit would walk from its activation start to a marker, cached per marker.
        // Falls back to the straight segment when no route exists, which restores the pre-#264
        // Euclidean measure for that objective rather than inventing a number.
        private (List<Position> Route, List<ITerrain> Terrain, float BaseRadius) RouteToObjective(
            Position now, Position marker)
        {
            var terrain = _tableState.Terrain.Objects.ToList();
            List<IModel> alive = _activeUnit!.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            float baseRadius = alive.Count == 0 ? 0.5f : alive.Max(m => m.BaseRadiusInches);

            (float X, float Z) key = (marker.x, marker.z);
            if (!_objectiveRoutes.TryGetValue(key, out List<Position>? route))
            {
                route = UnitRoute(terrain, now, marker, baseRadius);
                _objectiveRoutes[key] = route;
            }
            return (route, terrain, baseRadius);
        }

        // The route this unit would walk from its activation start to an enemy, cached per enemy -
        // the melee twin of RouteToObjective, sharing its grid. One A* per enemy per activation, and
        // only for enemies whose straight lane is actually blocked (RouteMetrics.Route short-circuits
        // a clear lane to the segment, so an open table pays nothing).
        private (List<Position> Route, List<ITerrain> Terrain, float BaseRadius) RouteToEnemy(
            Position now, DataBinding<UnitData> enemyBinding, Position enemyPos)
        {
            var terrain = _tableState.Terrain.Objects.ToList();
            List<IModel> alive = _activeUnit!.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
            float baseRadius = alive.Count == 0 ? 0.5f : alive.Max(m => m.BaseRadiusInches);

            if (!_enemyRoutes.TryGetValue(enemyBinding.Reference, out List<Position>? route))
            {
                route = UnitRoute(terrain, now, enemyPos, baseRadius);
                _enemyRoutes[enemyBinding.Reference] = route;
            }
            return (route, terrain, baseRadius);
        }

        /// <summary>
        /// The route THIS unit walks, honouring its own terrain-ignoring rules - the gradient must
        /// price the path the unit actually takes, not the one a footslogger would.
        /// <para>
        /// Flying (<see cref="ETerrainIgnoreScope.AllTerrain"/>) crosses impassible terrain, so its
        /// route is the straight segment and no grid is ever built: measuring an aircraft's approach
        /// around a wall it flies over charged it for a detour it never makes, which on the melee side
        /// also tripped the detour gate into the route measure when the straight line was exact.
        /// Strider (<see cref="ETerrainIgnoreScope.DifficultOnly"/>) still routes around walls but must
        /// not pay the router's difficult multiplier for ground it crosses at full speed - otherwise
        /// the score steers it around mud it should walk straight through, and disagrees with the move
        /// planner about where "toward the goal" lies.
        /// </para>
        /// </summary>
        private List<Position> UnitRoute(List<ITerrain> terrain, Position from, Position to,
            float baseRadius)
        {
            if (IgnoresAllTerrain) return new List<Position> { from, to };
            return RouteMetrics.Route(terrain,
                () => _routeGrid ??= TerrainGridCache.Get(_tableState, terrain, baseRadius, IgnoresDifficultTerrain),
                from, to, baseRadius);
        }

        // Terrain-ignoring rules are unit-wide and fixed for the activation, so they are read once
        // (the evaluator walk is not free) and reused by every route and candidate.
        private bool IgnoresAllTerrain => _ignoresAllTerrain ??=
            MovementRuleQueries.IgnoresAllTerrain(_activeUnit!.GetValue(), _evaluator);

        private bool IgnoresDifficultTerrain => _ignoresDifficultTerrain ??=
            MovementRuleQueries.IgnoresDifficultTerrain(_activeUnit!.GetValue(), _evaluator);

        private (float Margin, float Reach) MeleeApproachAgainst(DataBinding<UnitData> enemyBinding)
        {
            if (_meleeApproach.TryGetValue(enemyBinding.Reference, out (float, float) cached))
                return cached;

            UnitData self = _activeUnit!.GetValue();
            UnitData enemy = enemyBinding.GetValue();
            MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, _activeUnit, enemyBinding);
            (float, float) result = (
                ValueFraction(melee.AttackerAttack.ExpectedWounds, enemy)
                    - ValueFraction(melee.DefenderReturn.ExpectedWounds, self),
                TacticalAnalysis.ChargeDistanceAgainst(self, enemy, _evaluator));
            _meleeApproach[enemyBinding.Reference] = result;
            return result;
        }

        // +1 for each objective the endpoint newly holds/contests for us, -1 for a held objective the
        // move walks away from with nobody left on it. "Ours" is TEAM-owned (#296): victory pools
        // objectives per team (#257), and since #297 the engine's reconcile is team-aware too -
        // allied players guarding one marker HOLD it for their side (no ally-contest penalty or
        // step-off bonus needed any more; joining a teammate's marker is simply worth nothing extra).
        private float ObjectiveDelta(UnitData self, Position end)
        {
            float delta = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool projectedOurs = TacticalAnalysis.IsProjectedOwnerAllied(
                    _tableState, projection, self.PlayerID);
                bool projectedMine = projection.ProjectedOwner.HasValue
                    && projection.ProjectedOwner.Value == self.PlayerID;
                float endDist = Distance(end, projection.Objective.Position);
                float nowDist = TacticalAnalysis.MinBaseEdgeDistanceToPoint(self, projection.Objective.Position);
                bool weAreOnItNow = nowDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches;
                // The centroid is a coarse stand-in for base-edge reach; half the seize radius of slack.
                bool endOnIt = endDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f;

                if (!projectedOurs && endOnIt) delta += 1f;
                // Walking off a marker we hold: only priced when no TEAMMATE stays in range to keep
                // holding it for the side (#297) and an enemy can still reach it before game end.
                if (projectedMine && weAreOnItNow && !endOnIt
                    && !projection.PlayersInRange.Any(p => !p.Equals(self.PlayerID)
                        && TacticalAnalysis.AreAllied(_tableState, self.PlayerID, p))
                    && MarkerContestable(projection.Objective.Position))
                    delta -= 1f;
                // #296 support ball (Chris): close behind a marker is worth a fraction - the unit
                // stacks up to replace the front when it dies instead of drifting to a side goal.
                // Own/ally-held markers only pay while an enemy can still reach them (no late-game
                // loitering around a safe marker - the garrison-release rationale).
                bool endInSupportBand = !endOnIt
                    && endDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches + 6f;
                if (endInSupportBand
                    && (!projectedOurs || MarkerContestable(projection.Objective.Position)))
                    delta += TacticianWeights.MoveObjectiveSupport;
            }
            return delta;
        }

        // Garrison release (#191, Chris's game 3 - "even after the objective was 100% safe, they
        // still stayed"): ownership is sticky, so the walk-away penalty prices RE-CAPTURE risk -
        // which is zero once no living enemy can put a base within seizure range of the marker
        // before the game ends (rounds left x its best move, rush or charge). Anything alive but
        // off the battlefield (reserve, embarked cargo) could still land or spill out anywhere,
        // so its mere existence keeps every marker contestable - conservative, and moot by the
        // late rounds where the lock actually bites.
        private bool MarkerContestable(Position marker)
        {
            (float X, float Z) key = (marker.x, marker.z);
            if (_markerContestable.TryGetValue(key, out bool cached)) return cached;

            UnitData self = _activeUnit!.GetValue();
            int round = _tableState.Progress.RoundCount ?? 1;
            float movesLeft = _tableState.Progress.TotalRounds - round + 1;

            bool contestable = AnyEnemyOffBattlefield(self.PlayerID);
            if (!contestable)
            {
                foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(self.PlayerID))
                {
                    UnitData enemy = enemyBinding.GetValue();
                    if (AircraftRules.IsAircraft(enemy)) continue; // can never seize or contest
                    float reach = movesLeft * Math.Max(
                        TacticalAnalysis.RushDistance(enemy, _evaluator),
                        TacticalAnalysis.ChargeBudget(enemy, _evaluator));
                    if (TacticalAnalysis.MinBaseEdgeDistanceToPoint(enemy, marker)
                        <= reach + TacticalAnalysis.ObjectiveSeizureRadiusInches)
                    {
                        contestable = true;
                        break;
                    }
                }
            }
            _markerContestable[key] = contestable;
            return contestable;
        }

        // The most value-weighted damage this enemy could put on any friendly OTHER than the
        // active unit next activation, at the friendlies' current positions - the alternative
        // reply the retaliation share is measured against. Mirrors the incoming computation in
        // Score (shooting at post-advance reach; melee margin at half weight when the friendly is
        // inside charge threat) so the two sides of the share are the same currency. Candidate-
        // independent (friends are where they are), so one pass serves the whole activation.
        private float BestAlternativeTargetValue(DataBinding<UnitData> enemyBinding)
        {
            if (_enemyAltTarget.TryGetValue(enemyBinding.Reference, out float cached)) return cached;

            UnitData enemy = enemyBinding.GetValue();
            Position enemyPos = Centroid(enemy);
            float best = 0f;
            foreach (DataBinding<UnitData> friendlyBinding in FriendlyBindings(_activeUnit!.GetValue().PlayerID))
            {
                if (friendlyBinding.Reference.Equals(_activeUnit.Reference)) continue;
                UnitData friendly = friendlyBinding.GetValue();
                float d = Distance(enemyPos, Centroid(friendly));
                float reach = Math.Max(1f, d - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                float value = ValueFraction(CombatMath.EstimateShooting(_evaluator, enemyBinding,
                        friendlyBinding, new AttackContext(reach, AttackerMoved: true)).ExpectedWounds,
                    friendly);
                if (enemy.GetMeleeWeapons().Count > 0
                    && TacticalAnalysis.MeleeThreatReach(enemy, friendly, _evaluator) >= d - 1f)
                {
                    value = Math.Max(value, 0.5f * ValueFraction(CombatMath.EstimateMelee(
                        _evaluator, enemyBinding, friendlyBinding).AttackerAttack.ExpectedWounds,
                        friendly));
                }
                best = Math.Max(best, value);
            }
            _enemyAltTarget[enemyBinding.Reference] = best;
            return best;
        }

        // The projected-objective deficit (best-placed opposing SIDE minus ours), half a point of
        // tilt per marker, clamped to [-1, 1] and scaled by round progress: an early deficit is
        // deployment noise, a late one is the game. Positive = we are losing the race.
        // #296: tallied per TEAM - victory pools teammates' markers (#257), so a 2v2 posture that
        // counted the teammate as an opponent read "losing" while the team was ahead.
        private float Posture()
        {
            if (_posture.HasValue) return _posture.Value;

            PlayerID us = _activeUnit!.GetValue().PlayerID;
            int ours = 0;
            var byOpposingPlayer = new Dictionary<PlayerID, int>();
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                if (!projection.ProjectedOwner.HasValue) continue;
                PlayerID owner = projection.ProjectedOwner.Value;
                if (TacticalAnalysis.AreAllied(_tableState, us, owner)) ours++;
                else byOpposingPlayer[owner] = byOpposingPlayer.TryGetValue(owner, out int n) ? n + 1 : 1;
            }
            // Pool each opposing side's markers (players with no team pool alone).
            var byOpposingSide = new Dictionary<PlayerID, int>();
            foreach ((PlayerID owner, int n) in byOpposingPlayer)
            {
                PlayerID key = owner;
                foreach (PlayerID other in byOpposingSide.Keys)
                    if (TacticalAnalysis.AreAllied(_tableState, owner, other)) { key = other; break; }
                byOpposingSide[key] = byOpposingSide.TryGetValue(key, out int n2) ? n2 + n : n;
            }
            int bestOpponent = byOpposingSide.Count == 0 ? 0 : byOpposingSide.Values.Max();

            int round = _tableState.Progress.RoundCount ?? 1;
            float roundFraction = (float)round / Math.Max(1, _tableState.Progress.TotalRounds);
            _posture = Math.Clamp((bestOpponent - ours) * 0.5f, -1f, 1f) * roundFraction;
            return _posture.Value;
        }

        // Where this enemy will plausibly stand after its next activation: one rush-budget step
        // toward its nearest attractive goal - a marker its side does not own, or one of our
        // units. Deliberately a crude, deterministic proxy (it prices ARRIVING pressure, not a
        // prediction of play), and endpoint-independent so one projection serves the activation.
        private Position ProjectedEnemyPosition(DataBinding<UnitData> enemyBinding)
        {
            if (_enemyProjected.TryGetValue(enemyBinding.Reference, out Position cached)) return cached;

            UnitData enemy = enemyBinding.GetValue();
            Position at = Centroid(enemy);
            Position? goal = null;
            float bestDistance = float.MaxValue;
            foreach (IObjective objective in _tableState.Objectives.Objects)
            {
                if (objective.OwnerID.HasValue
                    && TacticalAnalysis.AreAllied(_tableState, enemy.PlayerID, objective.OwnerID.Value))
                    continue; // #296: a marker the enemy's SIDE holds is no goal of theirs
                float d = Distance(at, objective.Position);
                if (d < bestDistance) { bestDistance = d; goal = objective.Position; }
            }
            foreach (DataBinding<UnitData> friendly in FriendlyBindings(_activeUnit!.GetValue().PlayerID))
            {
                Position p = Centroid(friendly.GetValue());
                float d = Distance(at, p);
                if (d < bestDistance) { bestDistance = d; goal = p; }
            }

            Position result = at;
            if (goal != null && bestDistance > 0.01f)
            {
                float step = Math.Min(
                    TacticalAnalysis.RushDistance(enemy, _evaluator), bestDistance);
                result = new Position(at.x + (goal.Value.x - at.x) / bestDistance * step,
                    at.z + (goal.Value.z - at.z) / bestDistance * step);
            }
            _enemyProjected[enemyBinding.Reference] = result;
            return result;
        }

        // Longest effective weapon range this enemy has against the active unit - the cheap
        // precheck that keeps the arriving-pressure term from paying a CombatMath estimate for
        // every distant enemy at every candidate endpoint.
        private float EnemyMaxRangeAgainstUs(DataBinding<UnitData> enemyBinding)
        {
            if (_enemyMaxRange.TryGetValue(enemyBinding.Reference, out float cached)) return cached;
            float range = TacticalAnalysis.MaxWeaponRange(
                enemyBinding.GetValue(), _activeUnit!.GetValue(), _evaluator);
            _enemyMaxRange[enemyBinding.Reference] = range;
            return range;
        }

        private bool AnyEnemyOffBattlefield(PlayerID us)
        {
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (TacticalAnalysis.AreAllied(_tableState, us, army.PlayerID)
                    || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().Models.Any(m => m.GetIsAlive())
                        && !unit.GetValue().GetIsOnBattlefield())
                        return true;
            }
            return false;
        }

        // Keyed on the ACTION TYPE the executor will declare, not the intent: the engine's shoot
        // gate compares the actual moved distance to the advance-and-shoot cap, so only Hold and
        // Advance plans keep the volley. Keying on intent said Escort/SeekCoverFrom (both planned
        // as Rush) could shoot after - phantom offense that made gunline units "seek cover" or
        // "escort" every round instead of firing (#16 RL-row collapse: Warriors fired 0-1 times
        // per game; the RL row was the only one below the 50% gate line).
        private static bool CanShootAfter(MacroAction candidate) =>
            candidate.ActionType is EActionType.Hold or EActionType.Advance;

        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }

        // Charge maps to Charge; Hold maps to Shoot (stand and fire) or Pass; everything else moves.
        // Dispatch keys on the candidate's ACTION TYPE for charges: an out-of-reach M5 candidate is
        // a rush-budget approach (EActionType.Rush) and plays as a plain move (#191 A4 gate fix).
        private static string? ActionNameFor(MacroAction candidate, IReadOnlyList<string> validOptions)
        {
            if (candidate.ActionType == EActionType.Charge)
                return candidate.Feasibility == EFeasibility.Reachable
                    && validOptions.Contains(ChooseActionStage.CHARGE_CHOICE_NAME)
                    ? ChooseActionStage.CHARGE_CHOICE_NAME : null;

            switch (candidate.Intent)
            {
                case EMacroIntent.Hold:
                    if (validOptions.Contains(ChooseActionStage.SHOOT_CHOICE_NAME))
                        return ChooseActionStage.SHOOT_CHOICE_NAME;
                    return validOptions.Contains(ChooseActionStage.PASS_CHOICE_NAME)
                        ? ChooseActionStage.PASS_CHOICE_NAME : null;
                default:
                    return validOptions.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME)
                        ? ChooseActionStage.MOVEMENT_CHOICE_NAME : null;
            }
        }

        // #296: ENEMIES are the players not allied with us - in a 2v2 the old PlayerID comparison
        // put the teammate's 3k army in every threat/retaliation/approach term.
        private IEnumerable<DataBinding<UnitData>> EnemyBindings(PlayerID us)
        {
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (TacticalAnalysis.AreAllied(_tableState, us, army.PlayerID)
                    || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().Models.Any(m => m.GetIsAlive())
                        && unit.GetValue().GetIsOnBattlefield())
                        yield return unit;
            }
        }

        private static Position Centroid(UnitData unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return new Position(0f, 0f);
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        private static float Distance(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
