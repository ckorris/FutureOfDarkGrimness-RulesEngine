using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai.Tactician
{
    /// <summary>
    /// One objective's projected end-of-round state: who would own it if the round ended now.
    /// <see cref="ProjectedOwner"/> follows ReconcileObjectivesStage's rules (team-aware since
    /// #297): exactly one SIDE in range holds it (sticky toward the current owner, else the
    /// side's deterministic representative); opposing sides contest it back to neutral; nobody
    /// in range leaves the CURRENT owner in place (objectives are sticky).
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

        // --- Sides (#296) -----------------------------------------------------------------------

        /// <summary>
        /// Team-aware "same side" for the AI's enemy/friendly splits (#296): the Tactician priced a
        /// 2v2 TEAMMATE's whole army as hostile - retaliation from allied guns, FallBack from the
        /// highest-value ally, kite bands off allied shooters - because every enumeration was a raw
        /// PlayerID comparison. With no registered team a player is allied only with itself, so
        /// every 1v1 path (and benchmark) is identical by construction.
        /// </summary>
        public static bool AreAllied(ITableState tableState, PlayerID a, PlayerID b) =>
            ITeamExtensions.AreAllied(tableState.Teams.Objects, a, b);

        /// <summary>
        /// Whether the marker's projected owner is on <paramref name="player"/>'s side - the "ours"
        /// the objective terms should use: victory pools objectives per TEAM (#257), so a
        /// teammate-held marker is not one to march onto (two allied players in range contest it
        /// back to NEUTRAL under ReconcileObjectivesStage's per-player rules).
        /// </summary>
        public static bool IsProjectedOwnerAllied(ITableState tableState,
            ObjectiveProjection projection, PlayerID player) =>
            projection.ProjectedOwner.HasValue
            && AreAllied(tableState, player, projection.ProjectedOwner.Value);

        // --- Mobility / threat ------------------------------------------------------------------

        /// <summary>
        /// Terrain snapshot for the movement-budget queries (#376 Grounded Speed): terrain-gated
        /// movement rules only fire when the caller supplies the table's terrain, so every planner
        /// path that holds table state passes this down - otherwise the planner's numbers diverge
        /// from the move resolver's (the #264 issue-7 failure mode).
        /// </summary>
        public static IReadOnlyList<ITerrain> TerrainOf(ITableState tableState) =>
            tableState.Terrain.Objects.ToList();

        /// <summary>How far the unit may move and still shoot (Advance), movement rules included.</summary>
        public static float AdvanceDistance(IUnit unit, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null) =>
            MovementRuleQueries.EffectiveMoveShootDistance(unit, evaluator, terrain);

        /// <summary>The unit's longest legal non-charge move (Rush), movement rules included.</summary>
        public static float RushDistance(IUnit unit, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null) =>
            MovementRuleQueries.EffectiveMaxRushDistance(unit, evaluator, terrain);

        /// <summary>
        /// Charge reach against a specific target: the charger's own charge budget (base + its
        /// movement rules, e.g. Fast) further modified by target-conditioned rules (Melee Shrouding
        /// etc.) - composed exactly as DefinePathStage does (budget in, per-target query on top).
        /// </summary>
        public static float ChargeDistanceAgainst(IUnit charger, IUnit target, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null) =>
            MovementRuleQueries.EffectiveChargeDistanceAgainst(
                charger, target, ChargeBudget(charger, evaluator, terrain), evaluator);

        /// <summary>
        /// How far away the charger is genuinely DANGEROUS (#191 A5-6): its charge budget plus the
        /// 3" horizontal melee cylinder (#417; a charge only needs to END within melee range, not in
        /// base contact). Centroid-based callers get a little extra slack on top.
        /// </summary>
        public static float MeleeThreatReach(IUnit charger, IUnit target, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null) =>
            ChargeDistanceAgainst(charger, target, evaluator, terrain)
            + GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL;

        /// <summary>
        /// #442 (#421 / #432 / #435): the chance <paramref name="unit"/> PASSES a Morale Test it would take now -
        /// <see cref="MoraleUtilities.TakeMoraleTest(IGameContext, IUnit, MoraleUtilities.MoraleQuality, bool, float?, float?, EMoraleTestCause)"/>
        /// priced instead of rolled: the Quality from <see cref="MoraleUtilities.ResolveMoraleQuality(ITableState, IUnit)"/>
        /// (own, joined hero's, a Tough(9)+ Hero's 6" aura), the pre-test morale modifiers through the same
        /// <see cref="PreMoraleTestContext"/> (Hive Bond / Hold the Line / Valiant's "+1 when shot/charged from over
        /// 9"/6"", Banner's "+1 for a test shooting or melee triggered"), granted morale tokens (peeked, not spent),
        /// the 2-6 clamp, then a Fearless re-roll's 4+ second chance. A Shaken unit always fails (0); a failure
        /// a conversion rule turns into a pass (No Retreat) counts as a pass when the test's failure would Shake or
        /// Rout (<paramref name="failureCausesShakenOrRout"/>).
        /// </summary>
        public static float MoralePassChance(ITableState tableState, RuleEvaluator evaluator, IUnit unit,
            EMoraleTestCause cause, float? shotFromInches = null, float? chargedFromInches = null,
            bool failureCausesShakenOrRout = true)
        {
            if (unit.Tokens.HasToken(TokenType.Shaken))
                return failureCausesShakenOrRout && HasMoraleConversion(evaluator, unit) ? 1f : 0f;

            int quality = MoraleUtilities.ResolveMoraleQuality(tableState, unit).Quality;
            var modifiers = new RollModifierSink();
            modifiers.ApplyFrom(evaluator.EvaluateAllNamed(
                    new PreMoraleTestContext(unit, shotFromInches, chargedFromInches, cause), RuleParticipant.Actor(unit))
                .Select(tagged => tagged.Op).ToList());
            int rollNeeded = DiceUtilities.ClampSuccessRollNeeded(quality - modifiers.Net(ERollKind.Morale)
                - GrantedRollModifiers.PeekNet(unit, ERollKind.Morale));
            float pass = (7 - rollNeeded) / 6f;

            // The read-only named query: EvaluateAll would log each op and spend one-shot grants.
            List<RuleOperation> completeOps = evaluator.EvaluateAllNamed(new MoraleTestContext(unit),
                    RuleParticipant.Actor(unit, models: HeroStatRules.LivingModels(unit)))
                .Select(tagged => tagged.Op).ToList();
            var reroll = new RerollSink();
            reroll.ApplyFrom(completeOps);
            if (reroll.RerollMoraleOnFailure)
                pass += (1f - pass) * (7 - MoraleUtilities.FearlessRerollPassesOn) / 6f;
            if (failureCausesShakenOrRout && completeOps.Any(op => op is RuleOperation.PassMoraleTest))
                pass = 1f;
            return pass;
        }

        private static bool HasMoraleConversion(RuleEvaluator evaluator, IUnit unit) =>
            evaluator.EvaluateAllNamed(new MoraleTestContext(unit),
                    RuleParticipant.Actor(unit, models: HeroStatRules.LivingModels(unit)))
                .Any(tagged => tagged.Op is RuleOperation.PassMoraleTest);

        /// <summary>
        /// #442 (#446): whether <paramref name="unit"/> would END in Dangerous terrain - any living model's base, at the
        /// end of its <paramref name="move"/> entry (or where it stands when it has none, a Hold), overlapping a
        /// Dangerous piece: the "activates in it" test it then owes at the start of its next activation
        /// (<c>MovementExecutor.RollActivationDangerousTerrain</c>, the same base-overlap test). Flying/Aircraft
        /// ignore it, as there; the caller checks that.
        /// </summary>
        public static bool EndsInDangerousTerrain(IUnit unit, IReadOnlyList<ModelMoveEntry>? move,
            IReadOnlyList<ITerrain> terrain)
        {
            List<ITerrain>? dangerous = null;
            foreach (ITerrain piece in terrain)
                if (piece.TerrainType.HasFlag(ETerrainType.Dangerous)) (dangerous ??= new()).Add(piece);
            if (dangerous == null) return false;

            Dictionary<IModel, Position>? ends = null;
            if (move != null)
            {
                foreach (ModelMoveEntry entry in move)
                    if (entry.Positions.Count > 0)
                        (ends ??= new(ReferenceEqualityComparer.Instance))[entry.Model.GetValue()] = entry.Positions[^1];
            }
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                Position at = ends != null && ends.TryGetValue(model, out Position end) ? end : model.Position;
                var point = new Float2(at.x, at.z);
                foreach (ITerrain piece in dangerous)
                    if (SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, point, point, model.BaseShape,
                            model.Facing))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// #442 (#425 Cumbersome, "may only use Hold or Advance actions"): whether the unit may declare a Charge at
        /// all - the gate ChooseActionStage applies (<see cref="ActionRestrictionQueries.MayDeclare"/>). An ENEMY
        /// that may not charge is no charge threat, however good its melee weapons are; it still strikes back.
        /// </summary>
        public static bool MayCharge(IUnit unit, RuleEvaluator evaluator) =>
            ActionRestrictionQueries.MayDeclare(unit, evaluator, EActionType.Charge);

        /// <summary>
        /// #442 (#432's charge arms): the charge-origin distance the engine would record if
        /// <paramref name="charger"/> charged <paramref name="target"/> from where they stand now - closest living
        /// models base to base, vertical included, measured from the charger's transport while it is embarked
        /// (<c>UnitActionContext.SnapshotDistancesToEnemies</c>, the snapshot <c>CombatMetadata</c> carries).
        /// </summary>
        public static float ChargeOriginDistance(ITableState tableState, IUnit charger, IUnit target)
        {
            IUnit measureFrom = charger;
            if (TransportUtilities.GetTransportId(charger) is UnitID transportId)
            {
                foreach (IArmy army in tableState.Armies.Objects)
                {
                    if (army is not ArmyData data) continue;
                    foreach (DataBinding<UnitData> unit in data.UnitBindings)
                        if (unit.GetValue().ID == transportId) measureFrom = unit.GetValue();
                }
            }
            return UnitCompareUtilities.MinDistanceBetweenUnits(measureFrom, target, out _, out _,
                includeVertical: true);
        }

        /// <summary>
        /// #442: how far the unit's footprint reaches from its centroid - the farthest living model's centre plus its
        /// base radius. Turns a centroid distance into an approximate base-edge gap where only a hypothetical
        /// centroid exists (a candidate endpoint). 0 for a unit with no living model.
        /// </summary>
        public static float Spread(IUnit unit)
        {
            float sumX = 0f, sumZ = 0f;
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                sumX += model.Position.x;
                sumZ += model.Position.z;
                count++;
            }
            if (count == 0) return 0f;
            float cx = sumX / count, cz = sumZ / count, spread = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                float dx = model.Position.x - cx, dz = model.Position.z - cz;
                spread = Math.Max(spread, MathF.Sqrt(dx * dx + dz * dz) + model.BaseRadiusInches);
            }
            return spread;
        }

        /// <summary>
        /// The unit's charge move allowance including its movement rules - the same accumulation
        /// MovementActionContext performs (the "when" fired for all three actions onto one sink).
        /// </summary>
        public static float ChargeBudget(IUnit unit, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null)
        {
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
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
                        new Rules.Dispatch.Contexts.MoveActionDeclaredContext(unit, action, baseDistance,
                            terrain),
                        RuleParticipant.Actor(unit))
                    .Select(tagged => tagged.Op));
            }
            // #432: the unit's own charge range (Highborn's +2") is part of the charge move, as in
            // MovementActionContext.
            sink.Add(EActionType.Charge, RangeRuleQueries.OwnChargeRangeDelta(unit, evaluator));
            return GameWideConstants.CHARGE_DISTANCE_INCHES + sink.Net(EActionType.Charge);
        }
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.MoveQuery, __probe); }
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
        public static float ThreatRangeAgainst(IUnit unit, IUnit target, RuleEvaluator evaluator,
            IReadOnlyList<ITerrain>? terrain = null)
        {
            float shooting = MaxWeaponRange(unit, target, evaluator);
            float shootingThreat = shooting > 0f ? AdvanceDistance(unit, evaluator, terrain) + shooting : 0f;
            // #355: a ramming vehicle threatens its charge reach even with no melee weapon. #442: a unit that may
            // not charge (#425 Cumbersome) threatens nothing in melee.
            float meleeThreat = ChargeContactRules.CanFightInMelee(unit) && MayCharge(unit, evaluator)
                ? ChargeDistanceAgainst(unit, target, evaluator, terrain) : 0f;
            return Math.Max(shootingThreat, meleeThreat);
        }

        /// <summary>
        /// #389: how far along the straight line from <paramref name="start"/> toward
        /// <paramref name="toward"/> a model of radius <paramref name="moverRadius"/> could actually
        /// END a move, out of <paramref name="budget"/> inches. Friendly bases never block passage -
        /// a move may path through them (#205) - but a model may not stand overlapping one, so the
        /// answer is the farthest free stand-point: the full budget when the spot at that depth is
        /// clear (a thin screen with room behind it costs nothing), else the near edge of the
        /// occupied span the budget-depth spot falls in. Circle approximation (bounding radii), 2D;
        /// pass the living allied models of OTHER units as <paramref name="blockers"/>.
        /// </summary>
        public static float FreeStraightAdvance(Position start, Position toward, float moverRadius,
            float budget, IReadOnlyList<IModel> blockers)
        {
            if (budget <= 0f) return 0f;
            float dirX = toward.x - start.x, dirZ = toward.z - start.z;
            float length = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 0.001f) return budget;
            dirX /= length; dirZ /= length;

            // Spans of travel distance at which the mover's center would overlap a blocker's base.
            var occupied = new List<(float From, float To)>();
            foreach (IModel blocker in blockers)
            {
                float relX = blocker.Position.x - start.x, relZ = blocker.Position.z - start.z;
                float along = relX * dirX + relZ * dirZ;
                float combined = moverRadius + blocker.BaseRadiusInches;
                if (along + combined <= 0f || along - combined >= budget) continue;
                float lateralSq = relX * relX + relZ * relZ - along * along;
                float halfSq = combined * combined - lateralSq;
                if (halfSq <= 0f) continue;
                float half = MathF.Sqrt(halfSq);
                occupied.Add((along - half, along + half));
            }
            if (occupied.Count == 0) return budget;

            occupied.Sort((a, b) => a.From.CompareTo(b.From));
            var merged = new List<(float From, float To)>();
            foreach ((float From, float To) span in occupied)
            {
                if (merged.Count > 0 && span.From <= merged[^1].To)
                    merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, span.To));
                else
                    merged.Add(span);
            }
            foreach ((float From, float To) span in merged)
                if (span.From <= budget && budget <= span.To)
                    return Math.Max(0f, span.From);
            return budget;
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
        /// units, units that arrived from reserve this round, and Aircraft; ownership resolves
        /// per SIDE via <see cref="ITeamExtensions.ReconcileObjectiveOwner"/> (#297 - allied
        /// players guarding one marker hold it, only opposing sides contest it to neutral),
        /// none in range leaves the current owner.
        /// </summary>
        public static List<ObjectiveProjection> ProjectObjectives(ITableState tableState)
        {
            var __probe = global::FDG.Ai.Tactician.Search.SearchTiming.Start();
            try
            {
            var projections = new List<ObjectiveProjection>();
            foreach (IObjective objective in tableState.Objectives.Objects)
            {
                List<PlayerID> nearby = PlayersNearObjective(tableState, objective.Position);
                PlayerID? projected = ITeamExtensions.ReconcileObjectiveOwner(
                    tableState.Teams.Objects, objective.OwnerID, nearby);
                projections.Add(new ObjectiveProjection(objective, projected, nearby));
            }
            return projections;
        }
            finally { global::FDG.Ai.Tactician.Search.SearchTiming.Stop(global::FDG.Ai.Tactician.Search.SearchTiming.Stage.ObjectiveProj, __probe); }
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

        /// <summary>
        /// <see cref="MinBaseEdgeDistanceToPoint(IUnit, Position)"/> for a PLANNED move: the closest
        /// base-edge distance from any living model's END position (at its end facing, as the engine's
        /// own end-state checks measure - #312) to a point. <see cref="float.MaxValue"/> when the move
        /// carries no living model with a destination. #191 step 10 P4: the seize test the generator and
        /// the planner grade a candidate by - the CENTROID is not what the reconcile rules look at, and
        /// a sliver formation puts one model on the marker with its centroid deliberately far away.
        /// </summary>
        public static float MinEndBaseEdgeDistanceToPoint(IReadOnlyList<ModelMoveEntry> move, Position point)
        {
            float best = float.MaxValue;
            foreach (ModelMoveEntry entry in move)
            {
                ModelData model = entry.Model.GetValue();
                if (!model.GetIsAlive() || entry.Positions.Count == 0) continue;
                Position end = entry.Positions[^1];
                Float2 facing = entry.Facings != null && entry.Facings.Count > 0
                    ? entry.Facings[^1] : model.Facing;
                float distance = BaseShapeGeometry.SurfaceDistanceToPoint2D(model.BaseShape, end, facing, point);
                if (distance < best) best = distance;
            }
            return best;
        }

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
                    // #424: the seam, not the range - an Assault pistol is priced on both sides.
                    if (!weapon.IsRanged()) continue;
                    int referenceSave = DiceUtilities.ClampSuccessRollNeeded(4 + weapon.ArmorPenetration);
                    output += weapon.Attacks * hitChance * (referenceSave - 1) / 6f;
                }
            }
            return output;
        }

        /// <summary>
        /// Expected wounds of one melee exchange's attack half against a reference Q4/D4 target -
        /// the melee twin of <see cref="RangedOutputWounds"/> (#191 C1 encoder, docs/tactician-
        /// c1-schema.md sec 3's melee_share). Zero for units with no melee weapons.
        /// </summary>
        public static float MeleeOutputWounds(IUnit unit)
        {
            float hitChance = (7 - DiceUtilities.ClampSuccessRollNeeded(unit.Quality)) / 6f;
            float output = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (Weapon weapon in model.Weapons)
                {
                    if (!weapon.IsMelee()) continue; // #424: the seam - "range 0 or grants melee use"
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
        public static float UnitValueWithCargo(IUnit unit, ITableState tableState, RuleEvaluator evaluator)
        {
            float value = UnitValue(unit);
            if (!Rules.Dispatch.TransportUtilities.IsTransport(unit, evaluator)) return value;
            foreach (IUnit occupant in Rules.Dispatch.TransportUtilities.GetOccupants(
                unit, tableState.Units.Objects.ToList()))
            {
                value += UnitValue(occupant);
            }
            return value;
        }
    }
}
