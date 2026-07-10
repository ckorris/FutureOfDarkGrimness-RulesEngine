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
    public sealed class TacticianPlanner
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;

        // Belt-and-braces livelock guard on casting: every attempt burns tokens (win or lose), so
        // this can never bind in a legal game (the pool caps at 6) - it only bounds a broken loop.
        private const int MaxCastAttemptsPerActivation = 8;

        private DataBinding<UnitData>? _activeUnit;
        private MacroAction? _plan;
        private int _castAttempts;
        // Melee exchange margin + charge reach per enemy; both are endpoint-independent, so one
        // computation serves every candidate of the activation.
        private readonly Dictionary<DataReference, (float Margin, float Reach)> _meleeApproach = new();

        /// <summary>The unit whose activation is being planned (null between activations).</summary>
        public DataBinding<UnitData>? ActiveUnit => _activeUnit;

        public TacticianPlanner(ITableState tableState, RuleEvaluator evaluator)
        {
            _tableState = tableState;
            _evaluator = evaluator;
        }

        /// <summary>Called by the activation resolver the moment it picks the unit.</summary>
        public void BeginActivation(DataBinding<UnitData> unit)
        {
            _activeUnit = unit;
            _plan = null;
            _castAttempts = 0;
            _meleeApproach.Clear();
        }

        /// <summary>
        /// Plans this activation and answers Choose Action. Returns null when the planner has no
        /// claim (no known active unit, or its planned choice is not among the valid options) -
        /// the caller then falls back to solo behavior, never faulting the stage (G3).
        /// </summary>
        public string? ChooseAction(IReadOnlyList<string> validOptions)
        {
            if (_activeUnit == null) return null;

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

            foreach (MacroAction candidate in candidates)
            {
                string? action = ActionNameFor(candidate, validOptions);
                if (action == null) continue; // not executable under the currently valid actions

                float score = Score(candidate);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestAction = action;
                }
            }

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

        // The best strictly-positive-value spell the unit can afford right now, or null. The
        // engine gates the Cast option on ITS eligibility (affordable + legal target), so this
        // only decides whether casting is WORTH it.
        private RuntimeSpell? BestAffordableCast()
        {
            UnitData self = _activeUnit!.GetValue();
            ArmyData? army = SpellValuation.ArmyOf(_tableState, self.PlayerID);
            if (army == null) return null;

            int tokens = self.Tokens.GetTokenCount(TokenType.SpellTokens);
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
                    offense = Math.Max(offense,
                        ValueFraction(melee.AttackerAttack.ExpectedWounds, enemy)
                        - ValueFraction(melee.DefenderReturn.ExpectedWounds, self));
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
                    float gapNow = Distance(now, enemyPos) - reach;
                    if (margin > 0f && gapNow > 1f)
                    {
                        float gapEnd = Math.Max(0f, endDistance - reach);
                        approach = Math.Max(approach,
                            margin * Math.Clamp((gapNow - gapEnd) / gapNow, 0f, 1f));
                    }
                }

                // What the enemy could do to us at the endpoint next activation (their advance included).
                float theirReach = Math.Max(1f, endDistance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator));
                AttackEstimate incoming = CombatMath.EstimateShooting(_evaluator, enemyBinding, _activeUnit,
                    new AttackContext(theirReach, AttackerMoved: true));
                float incomingValue = ValueFraction(incoming.ExpectedWounds, self);
                // Melee threat: if they can charge the endpoint, count their melee margin too.
                if (TacticalAnalysis.ChargeDistanceAgainst(enemy, self, _evaluator) >= endDistance - 1f
                    && enemy.GetMeleeWeapons().Count > 0)
                {
                    incomingValue = Math.Max(incomingValue, 0.5f * ValueFraction(
                        CombatMath.EstimateMelee(_evaluator, enemyBinding, _activeUnit).AttackerAttack.ExpectedWounds, self));
                }
                retaliation = Math.Max(retaliation, incomingValue);
            }

            float objectiveDelta = ObjectiveDelta(self, end);
            float objectiveApproach = ObjectiveApproach(now, end);

            return TacticianWeights.MoveDamage * offense
                 - TacticianWeights.MoveRetaliation * retaliation
                 + TacticianWeights.MoveObjective * objectiveDelta
                 + TacticianWeights.MoveApproach * approach
                 + TacticianWeights.MoveObjectiveApproach * objectiveApproach
                 + (candidate.Feasibility == EFeasibility.Reachable ? TacticianWeights.MoveReachableBonus : 0f);
        }

        // A5-3: the gradient HALF of the objective term - the fraction of the gap to the nearest
        // not-ours objective this move closes. ObjectiveDelta pays only ON the marker; without a
        // gradient a unit two moves out never starts walking (the shooter-freeze mechanism from
        // the a5-2-gate loss reading, the melee-approach bug's twin).
        private float ObjectiveApproach(Position now, Position end)
        {
            float onIt = TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f;
            float best = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool ours = projection.ProjectedOwner.HasValue
                    && projection.ProjectedOwner.Value == _activeUnit!.GetValue().PlayerID;
                if (ours) continue;

                float gapNow = Distance(now, projection.Objective.Position) - onIt;
                if (gapNow <= 0f) continue; // already there: ObjectiveDelta pays, not the gradient
                float gapEnd = Math.Max(0f, Distance(end, projection.Objective.Position) - onIt);
                best = Math.Max(best, Math.Clamp((gapNow - gapEnd) / gapNow, 0f, 1f));
            }
            return best;
        }

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
        // move walks away from with nobody left on it.
        private float ObjectiveDelta(UnitData self, Position end)
        {
            float delta = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                bool projectedOurs = projection.ProjectedOwner.HasValue
                    && projection.ProjectedOwner.Value == self.PlayerID;
                float endDist = Distance(end, projection.Objective.Position);
                float nowDist = TacticalAnalysis.MinBaseEdgeDistanceToPoint(self, projection.Objective.Position);
                bool weAreOnItNow = nowDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches;
                // The centroid is a coarse stand-in for base-edge reach; half the seize radius of slack.
                bool endOnIt = endDist <= TacticalAnalysis.ObjectiveSeizureRadiusInches + 1.5f;

                if (!projectedOurs && endOnIt) delta += 1f;
                if (projectedOurs && weAreOnItNow && !endOnIt
                    && projection.PlayersInRange.Count(p => p == self.PlayerID) <= 1)
                    delta -= 1f;
            }
            return delta;
        }

        private static bool CanShootAfter(MacroAction candidate) => candidate.Intent switch
        {
            EMacroIntent.Hold => true,
            EMacroIntent.AdvanceOnObjective or EMacroIntent.EngageAtRange
                or EMacroIntent.SeekCoverFrom or EMacroIntent.MoveToCast or EMacroIntent.Escort => true,
            // Rush-budget intents give up shooting; charge damage is scored as melee above.
            _ => false,
        };

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

        private IEnumerable<DataBinding<UnitData>> EnemyBindings(PlayerID us)
        {
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (army.PlayerID == us || army is not ArmyData data) continue;
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
