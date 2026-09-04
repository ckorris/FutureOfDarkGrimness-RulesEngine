using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Activation order by urgency (#191 A4-1) instead of the solo bot's first-in-list: activate
    /// the unit with the most to gain (value-weighted kill opportunity), the most to flip
    /// (objective within this activation's reach), or the most to lose (value-weighted incoming
    /// threat) - act before the opponent's next activation removes the unit or its opportunity.
    /// </summary>
    public class TacticianActivationResolver : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly ITableState _tableState;
        // #376 (Grounded Speed): terrain snapshot for the mobility queries - see TacticianPlanner.
        private IReadOnlyList<ITerrain>? _terrainSnapshot;
        private IReadOnlyList<ITerrain> Terrain =>
            _terrainSnapshot ??= TacticalAnalysis.TerrainOf(_tableState);
        private readonly RuleEvaluator _evaluator;
        private readonly TacticianPlanner? _planner;
        // #389: the #384 LoS house rule (GameSettings.SeeThroughFriendlyUnits) - false (official
        // rules) makes the kill term's sight gate count other friendly units' bases as blockers,
        // the same way the planner's offense term does.
        private readonly bool _seeThroughFriendlyUnits;
        // #359 measurement: when set (--log-decisions), each pick is narrated with whether the
        // frontline bias DECIDED it - i.e. urgency alone would have picked someone else. The #296
        // bias is deliberately below every real urgency signal, so this is the direct check on
        // "does it still bind in the crowded shape it was built for". Null costs nothing.
        private readonly Action<string>? _decisionLog;

        public TacticianActivationResolver(ITableState tableState, RuleEvaluator evaluator,
            TacticianPlanner? planner = null, Action<string>? decisionLog = null,
            bool seeThroughFriendlyUnits = false)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _planner = planner;
            _decisionLog = decisionLog;
            _seeThroughFriendlyUnits = seeThroughFriendlyUnits;
        }

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException(
                    $"AI received a {nameof(ChooseUnitToActivateRequest)} with no valid options.");

            // #191 B1 5b: a prescribed activation is taken instead of the urgency argmax, but still
            // THROUGH this resolver - BeginActivation runs on the prescribed unit exactly as it does
            // on a scored one. Bypassing the resolver to answer at the wire is what B0 measured
            // diverging (finding 4): the planner is then never told which unit is acting and every
            // later request in the activation falls back to solo behavior. Scoring is skipped
            // entirely, which is where a simulated activation's policy cost goes.
            DataBinding<UnitData>? prescribed = _planner?.TakePrescribedUnit();
            if (prescribed != null)
            {
                // Answer with the ENGINE's own binding for that unit, not the caller's instance -
                // the stage looks the reply up against the options it handed out.
                foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
                {
                    if (!Matches(option.Option, prescribed)) continue;
                    _planner!.ReportPrescribedUnitOutcome(honored: true);
                    _planner.BeginActivation(option.Option);
                    return Task.FromResult(option.Option);
                }
                // Stale prescription (that unit is not activatable now): fall through and score, G3.
                // #191 B2: and say so, so search closes the edge instead of crediting it.
                _planner!.ReportPrescribedUnitOutcome(honored: false);
            }

            if (request.ValidOptions.Count == 1)
            {
                _planner?.BeginActivation(request.ValidOptions[0].Option);
                return Task.FromResult(request.ValidOptions[0].Option);
            }

            DataBinding<UnitData> best = request.ValidOptions[0].Option;
            float bestScore = float.NegativeInfinity;
            // The plain-urgency argmax, tracked alongside (same order, same strict-greater tie
            // rule, so the comparison is exact): when it differs, the frontline bias decided.
            DataBinding<UnitData> plainBest = request.ValidOptions[0].Option;
            float plainBestScore = float.NegativeInfinity;
            float bestFrontline = 0f;
            IReadOnlyList<ActivationScore> scores = ActivationScores(
                request.ValidOptions.Select(option => option.Option).ToList());
            for (int i = 0; i < scores.Count; i++)
            {
                DataBinding<UnitData> option = request.ValidOptions[i].Option;
                float urgency = scores[i].Urgency;
                float front = scores[i].Frontline;
                float score = scores[i].Score;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = option;
                    bestFrontline = front;
                }
                if (urgency > plainBestScore)
                {
                    plainBestScore = urgency;
                    plainBest = option;
                }
            }
            if (_decisionLog != null)
            {
                bool biasDecisive = !ReferenceEquals(best, plainBest);
                _decisionLog($"activate {best.GetValue().Name} score={bestScore:F4} " +
                    $"front={bestFrontline:F2} of {request.ValidOptions.Count}" +
                    (biasDecisive
                        ? $" bias-decisive (urgency alone picks {plainBest.GetValue().Name})"
                        : ""));
            }
            _planner?.BeginActivation(best);
            return Task.FromResult(best);
        }

        /// <summary>One activation option's ranking terms: the score the resolver picks by, and its parts.</summary>
        public readonly record struct ActivationScore(float Score, float Urgency, float Frontline);

        /// <summary>
        /// The A policy's activation ranking for a set of options, in the options' order (#191 B2:
        /// the search's level-1 prior, docs/tactician-b2-design.md sec 3.1). Exactly what
        /// <see cref="Resolve"/> picks by - urgency plus the #296 frontline bias - so a search that
        /// opens the top-scored unit first opens A's own unit; ties resolve to the first option, the
        /// resolver's strict-greater rule.
        /// </summary>
        public IReadOnlyList<ActivationScore> ActivationScores(IReadOnlyList<DataBinding<UnitData>> options)
        {
            var result = new List<ActivationScore>(options.Count);
            IReadOnlyDictionary<DataReference, float> frontline = FrontlineFractions(options);
            foreach (DataBinding<UnitData> option in options)
            {
                float urgency = Urgency(option);
                float front = frontline.GetValueOrDefault(option.Reference);
                result.Add(new ActivationScore(
                    urgency + TacticianWeights.ActivationFrontlineBias * front, urgency, front));
            }
            return result;
        }

        // A prescription may arrive as a different DataBinding instance for the same unit (it can
        // have crossed the wire, or been enumerated from another read of the store), so identity is
        // by Reference - the same rule TacticianPlanner.TakePlannedMove uses.
        private static bool Matches(DataBinding<UnitData> option, DataBinding<UnitData> prescribed) =>
            ReferenceEquals(option, prescribed) || option.Reference.Equals(prescribed.Reference);

        /// <summary>
        /// #296 front-first ordering (Chris's crowded-game remedy): each option's position along the
        /// axis from our unactivated mass toward the enemy mass, normalized to [0,1] across the
        /// options (rearmost 0, frontmost 1). When the real urgency terms are flat - the crowded
        /// round-1 shape - this makes the front rank move first and clear lanes for what's behind.
        /// </summary>
        private IReadOnlyDictionary<DataReference, float> FrontlineFractions(
            IReadOnlyList<DataBinding<UnitData>> options)
        {
            var result = new Dictionary<DataReference, float>();
            if (options.Count < 2) return result;

            PlayerID us = options[0].GetValue().PlayerID;
            float enemyX = 0f, enemyZ = 0f;
            int enemies = 0;
            foreach (DataBinding<UnitData> enemy in EnemyBindings(us))
            {
                Position p = Centroid(enemy.GetValue());
                enemyX += p.x; enemyZ += p.z; enemies++;
            }
            if (enemies == 0) return result;

            var centroids = new List<(DataReference Reference, Position At)>();
            float ownX = 0f, ownZ = 0f;
            foreach (DataBinding<UnitData> option in options)
            {
                // Embarked/reserve units sit at the (0,0) sentinel - leave them out of the axis and
                // normalization (they read fraction 0; the cargo bias already orders them late).
                if (!option.GetValue().GetIsOnBattlefield()) continue;
                Position p = Centroid(option.GetValue());
                centroids.Add((option.Reference, p));
                ownX += p.x; ownZ += p.z;
            }
            if (centroids.Count < 2) return result;
            float dirX = enemyX / enemies - ownX / centroids.Count;
            float dirZ = enemyZ / enemies - ownZ / centroids.Count;
            float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 0.001f) return result;
            dirX /= length; dirZ /= length;

            float min = float.MaxValue, max = float.MinValue;
            var projections = new List<(DataReference Reference, float Along)>();
            foreach ((DataReference reference, Position at) in centroids)
            {
                float along = at.x * dirX + at.z * dirZ;
                projections.Add((reference, along));
                min = Math.Min(min, along);
                max = Math.Max(max, along);
            }
            if (max - min < 0.001f) return result;
            foreach ((DataReference reference, float along) in projections)
                result[reference] = (along - min) / (max - min);
            return result;
        }

        /// <summary>
        /// The A4-1 urgency score. All terms are value-weighted fractions ("how much of that unit's
        /// worth changes hands"), so kill and threat compare on one scale; the flip term is a flat
        /// bonus because objectives decide games, not casualties.
        /// </summary>
        public float Urgency(DataBinding<UnitData> unitBinding)
        {
            UnitData unit = unitBinding.GetValue();
            Position here = Centroid(unit);
            float advance = TacticalAnalysis.AdvanceDistance(unit, _evaluator, Terrain);
            float rush = TacticalAnalysis.RushDistance(unit, _evaluator, Terrain);

            float kill = 0f;
            float threat = 0f;
            IReadOnlyList<IModel> laneBlockers = AlliedBlockerModels(unit);
            // #389 sight gate, the same pair the planner's offense term clears (#363 terrain +
            // #384 other friendly units' bases under official rules).
            List<ITerrain> sightBlockers = _seeThroughFriendlyUnits
                ? Terrain.ToList()
                : Terrain.Concat(LineOfSightUtilities.BuildFriendlySightBlockers(_tableState, unit))
                    .ToList();
            foreach (DataBinding<UnitData> enemyBinding in EnemyBindings(unit.PlayerID))
            {
                UnitData enemy = enemyBinding.GetValue();
                float distance = UnitCompareUtilities.MinDistanceBetweenUnits(unit, enemy,
                    out IModel? ourClosest, out IModel? theirClosest, includeVertical: true);

                // What could WE do to them this activation (shoot after advancing, at best range)?
                // #389: the volley must be one the unit could actually take. The assumed closure is
                // capped by the standing room on the straight lane (a shooter walled in by a deep
                // friendly mass cannot realize "advance and shoot"), and the shot is sight-tested
                // from the point that closure reaches - the planner's offense term already refused
                // to credit the phantom volley through a wall, but this term kept pricing it, so
                // the DEEPEST unit activated before its own lane-blockers and then had nowhere to
                // go (the WarriorSistersMovedLaterally save). Urgency is re-read at every pick, so
                // once a blocker moves away the discount lifts on its own.
                float closure = advance;
                bool hasLane = true;
                if (ourClosest != null && theirClosest != null)
                {
                    closure = Math.Min(advance, TacticalAnalysis.FreeStraightAdvance(
                        ourClosest.Position, theirClosest.Position, ourClosest.BaseRadiusInches,
                        advance, laneBlockers));
                    hasLane = LineOfSightUtilities.HasLineOfSight(
                        AdvancedBy(ourClosest.Position, theirClosest.Position, closure),
                        theirClosest.Position, sightBlockers);
                }
                float reachAfterAdvance = Math.Max(1f, distance - closure);
                AttackEstimate ours = CombatMath.EstimateShooting(_evaluator, unitBinding, enemyBinding,
                    new AttackContext(reachAfterAdvance, AttackerMoved: true,
                        SightFactor: hasLane ? 1f : 0f));
                kill = Math.Max(kill, ValueFraction(ours.ExpectedWounds, enemy));

                // What could THEY do to us from where things stand (their advance included)?
                float theirReach = Math.Max(1f,
                    distance - TacticalAnalysis.AdvanceDistance(enemy, _evaluator, Terrain));
                AttackEstimate theirs = CombatMath.EstimateShooting(_evaluator, enemyBinding, unitBinding,
                    new AttackContext(theirReach, AttackerMoved: true));
                threat = Math.Max(threat, ValueFraction(theirs.ExpectedWounds, unit));
            }

            float flip = 0f;
            foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(_tableState))
            {
                // #296: a TEAMMATE-held marker is already the side's (#297) - nothing to flip.
                bool oursAlready = TacticalAnalysis.IsProjectedOwnerAllied(
                    _tableState, projection, unit.PlayerID);
                if (oursAlready) continue;
                float distanceTo = TacticalAnalysis.MinBaseEdgeDistanceToPoint(unit, projection.Objective.Position);
                if (distanceTo <= rush + TacticalAnalysis.ObjectiveSeizureRadiusInches)
                {
                    flip = 1f;
                    break;
                }
            }

            // A5-6 (Chris): boat-then-payload - a loaded transport acts early so it can drive
            // before the cargo's own activation decides whether to get out; embarked cargo acts
            // late (nothing it does matters until the boat has moved).
            float transportBias = 0f;
            if (TransportUtilities.IsEmbarked(unit))
                transportBias = TacticianWeights.ActivationEmbarkedCargoBias;
            else if (TransportUtilities.IsTransport(unit, _evaluator)
                && TransportUtilities.GetOccupants(unit, _tableState.Units.Objects.ToList()).Any())
                transportBias = TacticianWeights.ActivationLoadedTransportBias;

            return TacticianWeights.ActivationKillOpportunity * kill
                 + TacticianWeights.ActivationObjectiveFlip * flip
                 + TacticianWeights.ActivationUnderThreat * threat
                 + transportBias;
        }

        // Expected wounds as a fraction of the target's remaining wounds, weighted by its value -
        // "how much unit-worth this trade moves".
        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }

        // #389: the point <paramref name="closure"/> inches along the lane from start toward the
        // target - where the assumed advance would actually leave the shooter, and therefore where
        // its sight line starts (a screen the mover can step past must not block the priced shot).
        private static Position AdvancedBy(Position start, Position toward, float closure)
        {
            float dirX = toward.x - start.x, dirZ = toward.z - start.z;
            float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 0.001f) return start;
            float t = Math.Min(closure, length);
            return new Position(start.x + dirX / length * t, start.z + dirZ / length * t);
        }

        // #389: every living allied model that could deny the acting unit standing room on its
        // advance lane - other units only (own and teammates'), on-battlefield. Activated or not:
        // standing room is physical, and a blocker that has NOT activated yet lowering this unit's
        // urgency is exactly the ordering we want (the blocker's own turn comes first).
        private IReadOnlyList<IModel> AlliedBlockerModels(UnitData unit)
        {
            var result = new List<IModel>();
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (!TacticalAnalysis.AreAllied(_tableState, unit.PlayerID, army.PlayerID)
                    || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> other in data.UnitBindings)
                {
                    UnitData value = other.GetValue();
                    if (ReferenceEquals(value, unit) || !value.GetIsOnBattlefield()) continue;
                    foreach (IModel model in value.Models)
                        if (model.GetIsAlive()) result.Add(model);
                }
            }
            return result;
        }

        // #296: team-aware - the urgency terms must not price a 2v2 teammate as kill or threat.
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
    }
}
