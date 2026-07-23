using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Objective-aware deployment (#191 A4b): the solo resolver's placement machinery already
    /// guarantees legality (cohesive block, in-zone, terrain/overlap/enemy clearance) and exposes
    /// exactly one strategy knob - the preferred block centre. This subclass re-aims that knob for
    /// DEPLOYMENT requests only: units spread across the objectives (nearest to the zone first)
    /// instead of marching blind lanes, melee units crowd the zone's forward edge, shooters hang
    /// back but stay in reach. Every other PlaceObjectsRequest (disembark, spillout, ambush
    /// arrival, reposition) and every fallback path keeps solo behavior via the base class.
    /// Cover-aware centre choice is deferred to a later A4b sub-slice (recorded in the ledger).
    /// </summary>
    public class TacticianPlaceObjectsResolver<T> : AiPlaceObjectsResolver<T>
    {
        /// <summary>DeployUnitStage's literal TaskName - the deployment discriminator.</summary>
        public const string DeploymentTaskName = "Place Unit Models";
        /// <summary>PlaceDeferredUnitsStage's literal - Scout placement into the forward-extended
        /// zone works exactly like deployment (#191 A5-2).</summary>
        public const string ScoutTaskName = "Place Scout Unit";
        /// <summary>StartOfRoundExtraActionStage's literal - Ambush arrival over the whole table.</summary>
        public const string AmbushTaskName = "Ambush Deploy";

        // How far behind the zone's forward edge a unit prefers to stand, by weapon reach.
        private const float LongRangeDepthInches = 6f;
        private const float MidRangeDepthInches = 3f;

        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;

        public TacticianPlaceObjectsResolver(ITableState tableState, RuleEvaluator? evaluator = null)
            : base(tableState)
        {
            _tableState = tableState;
            _evaluator = evaluator ?? new RuleEvaluator(new ProbabilisticDiceRoller());
        }

        protected override Position PreferredBlockCenter(PlaceObjectsRequest<T> request, ZoneBounds bounds,
            int deployIndex, float maxRadius, float gridWidth, float gridHeight, float spacingX)
        {
            bool deploymentShaped = request.TaskName == DeploymentTaskName
                || request.TaskName == ScoutTaskName;
            if (!deploymentShaped && request.TaskName != AmbushTaskName)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            List<IObjective> objectives = _tableState.Objectives.Objects.ToList();
            if (objectives.Count == 0)
                return base.PreferredBlockCenter(request, bounds, deployIndex, maxRadius,
                    gridWidth, gridHeight, spacingX);

            // Ambush arrival (#191 A5-2, revised A5-8 - Chris: "in real games they'll always pop
            // up right behind a unit that they'll do lots of damage to"): aim at the spot behind
            // the best STRIKE victim when one is worth it (gross damage over the bar, exchange
            // net-positive after retaliation); otherwise at the most winnable objective (not
            // ours, fewest enemies nearby, central as the tie break). The unit cannot score the
            // round it arrives, so a strike costs no scoring tempo. The engine enforces the
            // rule's enemy clearance (the caller spiral-searches off the aim).
            if (request.TaskName == AmbushTaskName)
            {
                Position? strike = BestStrikePosition(request);
                return strike ?? BestAmbushObjective(objectives, request).Position;
            }

            // Spread successive units across the objectives, closest to our zone first, so the army
            // fans out over what decides the game instead of over empty table.
            var zoneCenter = new Position(bounds.CenterX, bounds.CenterZ);
            objectives.Sort((a, b) =>
                DistSq(a.Position, zoneCenter).CompareTo(DistSq(b.Position, zoneCenter)));
            IObjective aim = objectives[deployIndex % objectives.Count];

            // A5-9 (Chris's option 2): matchup-weighted lane choice. Score each objective lane by
            // the VISIBLE enemy units roughly opposite it (favorability of us-vs-them, faded over
            // 18" of lateral offset); when some lane clearly beats the round-robin spread, take
            // it - the Deadly platform drifts opposite their Tough units, the gauss line opposite
            // the horde. With nothing deployed yet (or no clear signal) the spread stands.
            if (deploymentShaped)
            {
                IObjective? counter = BestMatchupLane(request, objectives, out float edge);
                if (counter != null && edge > MatchupLaneEdgeThreshold) aim = counter;
            }

            // Depth: the forward edge is the one facing the table centre (DefaultDeployFacing.Y is
            // the +-1 Z direction the zone looks toward). Melee crowds the line; shooters step back
            // without leaving reach of the mid-table.
            Float2 facing = PlacementUtilities.DefaultDeployFacing(bounds,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            float forwardZ = facing.Y >= 0f ? bounds.Top : bounds.Bottom;
            float z = forwardZ - facing.Y * DepthFor(MaxWeaponRange(request));

            // The caller clamps into the zone and spiral-searches for a clear block, so a raw aim
            // (the objective's X, our chosen depth) is safe even when the objective is off-zone.
            return ClearestRouteLane(new Position(aim.Position.x, z), aim.Position, bounds);
        }

        // #264 issue 8a: how much detour to the aim a lane may carry before it counts as walled off,
        // and the lateral probe pattern used to find a better one (the zone is 48" wide at most, so
        // 8 probes each way at 3" reach every part of it).
        private const float LaneDetourToleranceInches = 4f;
        private const float LaneProbeStepInches = 3f;
        private const int LaneProbeCount = 8;
        // Routes are judged for ONE model base, not the whole block: the question is whether the lane
        // exists at all, and the block's own legality is the caller's spiral search to settle.
        private const float LaneProbeRadiusInches = 0.5f;

        /// <summary>
        /// The deployment lane whose ROUTE to the aim is not walled off, nearest the preferred one.
        /// <para>
        /// #264 issue 8a: deployment aimed at objective lanes but only avoided terrain it would
        /// OVERLAP, so a big unit happily parked in a pocket behind a piece spanning its lane -
        /// self-trapping on turn zero, which is how Chris's Knight Brothers game started. Scoring the
        /// lane by path distance vs straight-line distance is the same measure the movement gradient
        /// uses (#264 issue 1), so deployment and movement now agree about what "toward the marker"
        /// means. Probes only run when there is impassible terrain to trip over.
        /// </para>
        /// </summary>
        private Position ClearestRouteLane(Position preferred, Position aim, ZoneBounds bounds)
        {
            var terrain = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();
            if (terrain.Count == 0) return preferred;

            TerrainGrid? grid = null;
            float Detour(Position from)
            {
                grid ??= TerrainGrid.Build(terrain, LaneProbeRadiusInches);
                List<Position>? route = GridPathfinder.FindPath(grid, terrain, from, aim,
                    LaneProbeRadiusInches);
                if (route == null) return float.PositiveInfinity; // no lane at all
                return RouteMetrics.Length(route) - Dist(from, aim);
            }

            float bestDetour = Detour(preferred);
            if (bestDetour <= LaneDetourToleranceInches) return preferred;

            Position best = preferred;
            for (int step = 1; step <= LaneProbeCount; step++)
            {
                foreach (float sign in new[] { 1f, -1f })
                {
                    float x = Math.Clamp(preferred.x + sign * step * LaneProbeStepInches,
                        bounds.Left, bounds.Right);
                    var probe = new Position(x, preferred.z);
                    float detour = Detour(probe);
                    if (detour < bestDetour)
                    {
                        bestDetour = detour;
                        best = probe;
                    }
                    if (bestDetour <= LaneDetourToleranceInches) return best;
                }
            }
            return best;
        }

        private static float Dist(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        // A lane must beat the default spread by a real margin before it overrides (score units:
        // fractions of a reference-100 unit's value).
        private const float MatchupLaneEdgeThreshold = 0.05f;
        private const float LaneHalfWidthInches = 18f;

        // A5-9: the matchup-best objective lane for the deploying unit, judged against enemies
        // already ON the battlefield. `edge` is best-lane score minus second-best - the override
        // only fires when the choice is clear, so blind early placements keep the spread.
        private IObjective? BestMatchupLane(PlaceObjectsRequest<T> request,
            List<IObjective> objectives, out float edge)
        {
            edge = 0f;
            DataBinding<UnitData>? us = OwningUnit(request);
            if (us == null) return null;

            var visibleEnemies = DeploymentMatchup.EnemyBindings(_tableState, request.TargetPlayerID)
                .Where(e => e.GetValue().GetIsOnBattlefield()).ToList();
            if (visibleEnemies.Count == 0) return null;

            IObjective? best = null;
            float bestScore = float.MinValue, second = float.MinValue;
            foreach (IObjective objective in objectives)
            {
                float score = 0f;
                foreach (DataBinding<UnitData> enemy in visibleEnemies)
                {
                    float lateral = Math.Abs(Centroid(enemy.GetValue()).x - objective.Position.x);
                    float weight = Math.Clamp(1f - lateral / LaneHalfWidthInches, 0f, 1f);
                    if (weight <= 0f) continue;
                    score += weight * DeploymentMatchup.Favorability(_evaluator, us, enemy);
                }
                if (score > bestScore) { second = bestScore; bestScore = score; best = objective; }
                else if (score > second) { second = score; }
            }
            edge = objectives.Count < 2 ? bestScore : bestScore - second;
            return best;
        }

        // A5-8: the strike aim - for each enemy unit, a landing spot just over the rule's enemy
        // clearance on the side AWAY from the rest of their army ("right behind it"), scored by
        // what the arriver does to the victim next activation (best of shooting from the spot and
        // a charge if the victim is in charge-threat reach) minus the usual retaliation price.
        // Null when no victim clears the gross-damage bar with a net-positive exchange.
        private Position? BestStrikePosition(PlaceObjectsRequest<T> request)
        {
            DataBinding<UnitData>? arriver = OwningUnit(request);
            if (arriver == null) return null;

            var enemies = new List<DataBinding<UnitData>>();
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (army.PlayerID == request.TargetPlayerID || army is not ArmyData data) continue;
                foreach (DataBinding<UnitData> unit in data.UnitBindings)
                    if (unit.GetValue().GetIsAlive() && unit.GetValue().GetIsOnBattlefield())
                        enemies.Add(unit);
            }
            if (enemies.Count == 0) return null;

            float clearance = Math.Max(1f, request.MinDistanceFromEnemiesInches) + 0.6f;
            Position? best = null;
            float bestNet = 0f;
            foreach (DataBinding<UnitData> victim in enemies)
            {
                Position spot = SpotBehind(victim, enemies, clearance);
                float distance = MathF.Sqrt(DistSq(spot, Centroid(victim.GetValue())));

                float damage = 0f;
                AttackEstimate shot = CombatMath.EstimateShooting(_evaluator, arriver, victim,
                    new AttackContext(distance, AttackerMoved: true));
                damage = Math.Max(damage, ValueFraction(shot.ExpectedWounds, victim.GetValue()));
                if (TacticalAnalysis.MeleeThreatReach(arriver.GetValue(), victim.GetValue(), _evaluator)
                    >= distance)
                {
                    MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, arriver, victim);
                    damage = Math.Max(damage,
                        ValueFraction(melee.AttackerAttack.ExpectedWounds, victim.GetValue())
                        - ValueFraction(melee.DefenderReturn.ExpectedWounds, arriver.GetValue()));
                }
                if (damage < TacticianWeights.AmbushStrikeMinDamageValue) continue;

                float net = damage - TacticianWeights.MoveRetaliation * RetaliationAt(spot, arriver, enemies);
                if (net <= bestNet) continue;
                bestNet = net;
                best = spot;
            }
            return best;
        }

        // The landing spot: just past the clearance from the victim, on the side facing away from
        // the mass of the REST of their army (their backfield); away from the table centre when
        // the victim is their only unit.
        private static Position SpotBehind(DataBinding<UnitData> victim,
            List<DataBinding<UnitData>> enemies, float clearance)
        {
            Position victimPos = Centroid(victim.GetValue());
            float massX = 0f, massZ = 0f;
            int others = 0;
            foreach (DataBinding<UnitData> enemy in enemies)
            {
                if (ReferenceEquals(enemy, victim)) continue;
                Position p = Centroid(enemy.GetValue());
                massX += p.x; massZ += p.z; others++;
            }
            Position awayFrom = others > 0
                ? new Position(massX / others, massZ / others)
                : new Position(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f,
                    GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);

            float dx = victimPos.x - awayFrom.x, dz = victimPos.z - awayFrom.z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length < 1e-3f) { dx = 1f; dz = 0f; length = 1f; }
            return new Position(
                Math.Clamp(victimPos.x + dx / length * clearance, 0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES),
                Math.Clamp(victimPos.z + dz / length * clearance, 0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES));
        }

        // The planner's retaliation term, reshaped for a landing spot: the worst single enemy
        // answer (their shooting with their advance folded in; half the melee margin if the spot
        // is inside their charge-threat reach).
        private float RetaliationAt(Position spot, DataBinding<UnitData> arriver,
            List<DataBinding<UnitData>> enemies)
        {
            float worst = 0f;
            foreach (DataBinding<UnitData> enemy in enemies)
            {
                float distance = MathF.Sqrt(DistSq(spot, Centroid(enemy.GetValue())));
                float reach = Math.Max(1f, distance
                    - TacticalAnalysis.AdvanceDistance(enemy.GetValue(), _evaluator));
                AttackEstimate incoming = CombatMath.EstimateShooting(_evaluator, enemy, arriver,
                    new AttackContext(reach, AttackerMoved: true));
                float value = ValueFraction(incoming.ExpectedWounds, arriver.GetValue());
                if (enemy.GetValue().GetMeleeWeapons().Count > 0
                    && TacticalAnalysis.MeleeThreatReach(enemy.GetValue(), arriver.GetValue(), _evaluator)
                        >= distance - 1f)
                {
                    value = Math.Max(value, 0.5f * ValueFraction(
                        CombatMath.EstimateMelee(_evaluator, enemy, arriver).AttackerAttack.ExpectedWounds,
                        arriver.GetValue()));
                }
                worst = Math.Max(worst, value);
            }
            return worst;
        }

        private DataBinding<UnitData>? OwningUnit(PlaceObjectsRequest<T> request)
        {
            ArmyData? army = SpellValuation.ArmyOf(_tableState, request.TargetPlayerID);
            if (army == null || request.ModelsToPlace.Count == 0) return null;
            if (request.ModelsToPlace[0] is not DataBinding<ModelData> firstModel) return null;
            foreach (DataBinding<UnitData> unit in army.UnitBindings)
                foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
                    if (model.Reference.Equals(firstModel.Reference))
                        return unit;
            return null;
        }

        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }

        private static Position Centroid(UnitData unit)
        {
            float x = 0f, z = 0f;
            int living = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                x += model.Position.x; z += model.Position.z; living++;
            }
            return living == 0 ? new Position(0f, 0f) : new Position(x / living, z / living);
        }

        // Not-ours first, then fewest living enemy models within the contest radius, then nearest
        // the table centre - all deterministic comparisons on live state.
        private IObjective BestAmbushObjective(List<IObjective> objectives, PlaceObjectsRequest<T> request)
        {
            var tableCentre = new Position(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);
            IObjective best = objectives[0];
            (int Ours, int Enemies, float CentreDistSq) bestKey = (int.MaxValue, int.MaxValue, float.MaxValue);
            foreach (IObjective objective in objectives)
            {
                (int, int, float) key = (
                    objective.OwnerID.HasValue && objective.OwnerID.Value == request.TargetPlayerID ? 1 : 0,
                    EnemiesNear(objective.Position, request.TargetPlayerID),
                    DistSq(objective.Position, tableCentre));
                if (key.CompareTo(bestKey) < 0)
                {
                    bestKey = key;
                    best = objective;
                }
            }
            return best;
        }

        private int EnemiesNear(Position point, PlayerID us)
        {
            const float contestRadiusInches = 9f;
            int count = 0;
            foreach (IUnit unit in _tableState.Units.Objects)
            {
                if (unit.PlayerID == us || !unit.GetIsOnBattlefield()) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (DistSq(model.Position, point) <= contestRadiusInches * contestRadiusInches)
                        count++;
                }
            }
            return count;
        }

        private static float DepthFor(float maxWeaponRange) => maxWeaponRange switch
        {
            >= 18f => LongRangeDepthInches,
            >= 12f => MidRangeDepthInches,
            _ => 0f,
        };

        private static float MaxWeaponRange(PlaceObjectsRequest<T> request)
        {
            float max = 0f;
            foreach (DataBinding<T> binding in request.ModelsToPlace)
            {
                if (binding.GetValue() is not ModelData model) continue;
                foreach (Weapon weapon in model.Weapons)
                    if (weapon.RangeInches > max) max = weapon.RangeInches;
            }
            return max;
        }

        private static float DistSq(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
