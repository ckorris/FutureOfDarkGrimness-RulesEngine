using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// One objective's projected end-of-round state: who would own it if the round ended now.
    /// <see cref="ProjectedOwner"/> follows ReconcileObjectivesStage's rules - exactly one player in
    /// range seizes; more than one contests it back to neutral; nobody in range leaves the CURRENT
    /// owner in place (objectives are sticky).
    /// </summary>
    public sealed record ObjectiveProjection(
        IObjective Objective,
        PlayerID? ProjectedOwner,
        IReadOnlyList<PlayerID> PlayersInRange);

    /// <summary>
    /// Shared board-reading queries for the Tactician (#191 A2, plan sec. 8): unit mobility and threat
    /// ranges, objective-control projection, and a rough unit value model. Everything here is
    /// read-only (evaluations go through the engine's non-consuming query path) and position-honest:
    /// distances are base-edge distances where the engine's own checks are (#150).
    /// </summary>
    public static class TacticalAnalysis
    {
        /// <summary>Mirror of ReconcileObjectivesStage.SeizureRadiusInches (private there; pinned by test).</summary>
        public const float ObjectiveSeizureRadiusInches = 3f;

        // --- Mobility / threat ------------------------------------------------------------------

        /// <summary>How far the unit may move and still shoot (Advance), movement rules included.</summary>
        public static float AdvanceDistance(IUnit unit, RuleEvaluator evaluator) =>
            MovementRuleQueries.EffectiveMoveShootDistance(unit, evaluator);

        /// <summary>The unit's longest legal non-charge move (Rush), movement rules included.</summary>
        public static float RushDistance(IUnit unit, RuleEvaluator evaluator) =>
            MovementRuleQueries.EffectiveMaxRushDistance(unit, evaluator);

        /// <summary>
        /// Charge reach against a specific target: the charger's own charge budget (base + its
        /// movement rules, e.g. Fast) further modified by target-conditioned rules (Melee Shrouding
        /// etc.) - composed exactly as DefinePathStage does (budget in, per-target query on top).
        /// </summary>
        public static float ChargeDistanceAgainst(IUnit charger, IUnit target, RuleEvaluator evaluator) =>
            MovementRuleQueries.EffectiveChargeDistanceAgainst(
                charger, target, ChargeBudget(charger, evaluator), evaluator);

        /// <summary>
        /// How far away the charger is genuinely DANGEROUS (#191 A5-6): its charge budget plus the
        /// 2" horizontal melee cylinder (a charge only needs to END within melee range, not in
        /// base contact). Centroid-based callers get a little extra slack on top.
        /// </summary>
        public static float MeleeThreatReach(IUnit charger, IUnit target, RuleEvaluator evaluator) =>
            ChargeDistanceAgainst(charger, target, evaluator)
            + GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL;

        /// <summary>
        /// The unit's charge move allowance including its movement rules - the same accumulation
        /// MovementActionContext performs (the "when" fired for all three actions onto one sink).
        /// </summary>
        public static float ChargeBudget(IUnit unit, RuleEvaluator evaluator)
        {
            var sink = new MovementModifierSink();
            foreach ((EActionType action, float baseDistance) in new[]
            {
                (EActionType.Advance, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES),
                (EActionType.Rush, GameWideConstants.RUSH_DISTANCE_INCHES),
                (EActionType.Charge, GameWideConstants.CHARGE_DISTANCE_INCHES),
            })
            {
                sink.ApplyFrom(evaluator.EvaluateAllNamed(
                        new Rules.Dispatch.Contexts.MoveActionDeclaredContext(unit, action, baseDistance),
                        RuleParticipant.Actor(unit))
                    .Select(tagged => tagged.Op));
            }
            return GameWideConstants.CHARGE_DISTANCE_INCHES + sink.Net(EActionType.Charge);
        }

        /// <summary>The longest effective reach among the unit's ranged weapons against this target (0 if none).</summary>
        public static float MaxWeaponRange(IUnit unit, IUnit target, RuleEvaluator evaluator)
        {
            float best = 0f;
            foreach (Weapon weapon in unit.GetRangedWeapons())
                best = Math.Max(best, RangeRuleQueries.EffectiveRange(unit, weapon, target, evaluator));
            return best;
        }

        /// <summary>
        /// The distance out to which <paramref name="unit"/> can hurt <paramref name="target"/> THIS
        /// activation: the larger of its shooting envelope (advance + longest effective weapon range)
        /// and its charge reach. The kite band (Appendix A, M4 SafeShooting) positions just outside
        /// the enemy's value of this.
        /// </summary>
        public static float ThreatRangeAgainst(IUnit unit, IUnit target, RuleEvaluator evaluator)
        {
            float shooting = MaxWeaponRange(unit, target, evaluator);
            float shootingThreat = shooting > 0f ? AdvanceDistance(unit, evaluator) + shooting : 0f;
            float meleeThreat = unit.GetMeleeWeapons().Count > 0
                ? ChargeDistanceAgainst(unit, target, evaluator) : 0f;
            return Math.Max(shootingThreat, meleeThreat);
        }

        /// <summary>Expected shooting damage at a hypothetical distance/cover - CombatMath, positioned.</summary>
        public static AttackEstimate ExpectedShootingAt(RuleEvaluator evaluator,
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            float distanceInches, bool defenderInCover = false, bool attackerMoved = false) =>
            CombatMath.EstimateShooting(evaluator, attacker, defender,
                new AttackContext(distanceInches, attackerMoved, defenderInCover));

        // --- Objectives ---------------------------------------------------------------------------

        /// <summary>
        /// Projects every objective's end-of-round owner from current positions - the exact
        /// ReconcileObjectivesStage rules: living models' base edges within 3", excluding Shaken
        /// units, units that arrived from reserve this round, and Aircraft; exactly one player in
        /// range seizes, several contest to neutral, none leaves the current owner.
        /// </summary>
        public static List<ObjectiveProjection> ProjectObjectives(ITableState tableState)
        {
            var projections = new List<ObjectiveProjection>();
            foreach (IObjective objective in tableState.Objectives.Objects)
            {
                List<PlayerID> nearby = PlayersNearObjective(tableState, objective.Position);
                PlayerID? projected = nearby.Count == 1 ? nearby[0]
                    : nearby.Count > 1 ? null
                    : objective.OwnerID;
                projections.Add(new ObjectiveProjection(objective, projected, nearby));
            }
            return projections;
        }

        /// <summary>The score <paramref name="player"/> would have if the round ended now.</summary>
        public static int ProjectedScore(ITableState tableState, PlayerID player) =>
            ProjectObjectives(tableState).Count(projection => projection.ProjectedOwner.Equals(player));

        /// <summary>
        /// Players with a seize/contest-eligible living model within the seizure radius of
        /// <paramref name="point"/> (the same eligibility filter the engine's reconcile uses).
        /// </summary>
        public static List<PlayerID> PlayersNearObjective(ITableState tableState, Position point)
        {
            var result = new List<PlayerID>();
            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (result.Contains(unit.PlayerID)) continue;
                if (!CanSeizeObjectives(unit)) continue;
                if (MinBaseEdgeDistanceToPoint(unit, point) <= ObjectiveSeizureRadiusInches)
                    result.Add(unit.PlayerID);
            }
            return result;
        }

        /// <summary>ReconcileObjectivesStage's eligibility gate: Shaken, fresh-from-reserve, and Aircraft units count toward nothing.</summary>
        public static bool CanSeizeObjectives(IUnit unit) =>
            !unit.Tokens.HasToken(TokenType.Shaken)
            && !unit.Tokens.HasToken(TokenType.ArrivedFromReserve)
            && !AircraftRules.IsAircraft(unit);

        /// <summary>Closest base-edge distance from any living model of the unit to a point (true footprint, #150).</summary>
        public static float MinBaseEdgeDistanceToPoint(IUnit unit, Position point)
        {
            float best = float.MaxValue;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                float distance = BaseShapeGeometry.SurfaceDistanceToPoint2D(
                    model.BaseShape, model.Position, model.Facing, point);
                if (distance < best) best = distance;
            }
            return best;
        }

        // --- Unit value ------------------------------------------------------------------------------

        /// <summary>
        /// A rough "how much is this unit worth" scalar (#191 A2). Runtime units carry no point cost
        /// (UnitFileEntry.PointCost never reaches UnitData), so this is the plan's fallback formula:
        /// f(wounds, quality, weapon output) - durability (expected AP-0 hits to kill through the
        /// save) times offensive output (expected wounds/activation against a reference Q4/D4 target),
        /// geometric mean so glass cannons and unarmed walls both price sensibly. Calibrated for
        /// ORDERING against book points (TacticalAnalysisTests), not for absolute values; special
        /// rules deliberately don't contribute yet (recorded gap - revisit if A4's value-weighted
        /// targeting misprices rule-heavy units in benchmark reading, G2).
        /// </summary>
        public static float UnitValue(IUnit unit)
        {
            float failChance = (DiceUtilities.ClampSuccessRollNeeded(unit.Defense) - 1) / 6f;
            float durability = unit.RemainingWounds / Math.Max(failChance, 1f / 6f);

            float output = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (Weapon weapon in model.Weapons)
                {
                    float hitChance = (7 - DiceUtilities.ClampSuccessRollNeeded(unit.Quality)) / 6f;
                    int referenceSave = DiceUtilities.ClampSuccessRollNeeded(4 + weapon.ArmorPenetration);
                    float unsavedChance = (referenceSave - 1) / 6f;
                    output += weapon.Attacks * hitChance * unsavedChance;
                }
            }

            return MathF.Sqrt(durability * (1f + output)) * 10f;
        }

        /// <summary>
        /// Expected wounds of one ranged volley against a reference Q4/D4 target - the shooting
        /// half of the <see cref="UnitValue"/> output loop (#191 A5-8). Prices what tying this
        /// unit up in melee degrades; zero for melee-only units.
        /// </summary>
        public static float RangedOutputWounds(IUnit unit)
        {
            float hitChance = (7 - DiceUtilities.ClampSuccessRollNeeded(unit.Quality)) / 6f;
            float output = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (Weapon weapon in model.Weapons)
                {
                    if (weapon.RangeInches <= 0f) continue;
                    int referenceSave = DiceUtilities.ClampSuccessRollNeeded(4 + weapon.ArmorPenetration);
                    output += weapon.Attacks * hitChance * (referenceSave - 1) / 6f;
                }
            }
            return output;
        }

        /// <summary>
        /// <see cref="UnitValue"/> plus the value of anything riding inside (#191 A5-6): a loaded
        /// transport is worth boat + payload - both when choosing what to protect and when choosing
        /// what to shoot (destroying it spills the cargo out Shaken).
        /// </summary>
        public static float UnitValueWithCargo(IUnit unit, ITableState tableState)
        {
            float value = UnitValue(unit);
            if (!Rules.Dispatch.TransportUtilities.IsTransport(unit)) return value;
            foreach (IUnit occupant in Rules.Dispatch.TransportUtilities.GetOccupants(
                unit, tableState.Units.Objects.ToList()))
            {
                value += UnitValue(occupant);
            }
            return value;
        }
    }
}
