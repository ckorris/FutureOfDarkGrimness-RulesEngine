using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System.Runtime.CompilerServices;

namespace FDG.Stages
{
    public static class MovementUtilities
    {
        public static float GetMaxMoveDistance(List<ModelMoveEntry> moves)
        {
            Dictionary<ModelMoveEntry, float> distances = GetTotalMoveDistances(moves);
            return distances.Values.Max();
        }

        /// <summary>
        /// #333: how far one model travels along its own path — start position through every waypoint.
        /// Public because a front end has to be able to ask "did THIS model move?" before it commits (the
        /// CLI's Done confirmation), and re-deriving the sum caller-side is exactly how it drifts from the
        /// number <see cref="GetMaxMoveDistance"/> reports to the stage.
        /// </summary>
        public static float GetTotalMoveDistance(ModelMoveEntry move)
        {
            List<Position> path = move.Positions;
            if (path.Count == 0) return 0.0f;

            //Start to the first step, then along the rest.
            float distanceMoved = Position.GetDistance3D(move.Model.GetValue().PositionBinding, path[0]);
            for (int i = 0; i < path.Count - 1; i++)
            {
                distanceMoved += Position.GetDistance3D(path[i], path[i + 1]);
            }
            return distanceMoved;
        }

        // Normalise an optional footprint sequence to a non-null read-only list (empty when null).
        private static IReadOnlyList<EnemyModelFootprint> AsReadOnly(IEnumerable<EnemyModelFootprint>? footprints)
            => footprints as IReadOnlyList<EnemyModelFootprint> ?? footprints?.ToList()
               ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, maxDistanceInches, terrain: null, out errors);

        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
        {
            errors = new List<ReasonForInvalidMove>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            //This overload is only reached with terrain: null (the no-terrain convenience form), so the terrain
            //checks are no-ops regardless — no Strider/Flying flags to thread here.
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain: false, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain: false, ref errors);
            //No enemy footprints supplied (this overload predates enemy-aware validation): the move-through /
            //standoff check is a no-op here, preserving these callers' existing behavior.
            ValidateMovingThroughEnemyUnits(moves, Array.Empty<EnemyModelFootprint>(), canMoveThroughEnemies: false, ref errors);
            ValidateCoherency(moves, ref errors);
            ValidateEndsOnTable(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Enemy-aware validation WITHOUT charge semantics: terrain + move-through / standoff (honoring
        /// <paramref name="canMoveThroughEnemies"/>) + coherency, but no charge-reach requirement. For
        /// consolidation and out-of-band executor moves — they have their own distance cap and never charge.
        /// <paramref name="ignoresDifficultTerrain"/> waives the difficult-terrain move cap (Strider/Flying);
        /// <paramref name="ignoresImpassibleTerrain"/> waives the impassible-terrain block (Flying).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors,
            IEnumerable<EnemyModelFootprint>? friendlyFootprints = null)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherency(moves, ref errors);
            ValidateEndsOnFriendly(moves, AsReadOnly(friendlyFootprints), ref errors);
            ValidateEndsOnTable(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Back-compat charge overload that assumes the mover may not move through enemies
        /// (<c>canMoveThroughEnemies: false</c>) nor ignore difficult/impassible terrain
        /// (<c>false</c>). Kept so callers/tests that never need those rule-derived flags don't have to thread
        /// them (mirrors the no-enemy convenience overloads above).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, maxRushDistance, maxDistanceInches, enemyFootprints,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain, out errors);

        /// <summary>
        /// Full Move-action validation: paths must stay within the hard cap (Charge distance),
        /// and any path that exceeds the Rush distance requires at least one model to end within
        /// melee range of an enemy model. <paramref name="canMoveThroughEnemies"/> waives the
        /// pass-through block for fly-over units (Strafing); <paramref name="ignoresDifficultTerrain"/>
        /// waives the difficult-terrain move cap (Strider/Flying); <paramref name="ignoresImpassibleTerrain"/>
        /// waives the impassible-terrain block (Flying).
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            float maxRushDistance, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors)
            => ValidatePaths(moves, _ => new ModelMoveBudget(maxRushDistance, maxDistanceInches),
                enemyFootprints, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                terrain, out errors);

        /// <summary>
        /// Per-model form of the full Move-action validation (#093): each model is capped by its OWN
        /// <see cref="ModelMoveBudget"/> (a joined hero's Fast/Slow gives it a different budget than the rest
        /// of the unit) instead of one unit-wide pair of scalars. Coherency still reins a fast model in.
        /// The scalar overload above delegates here with the same budget for every model, so unit-wide
        /// callers are unchanged.
        ///
        /// <para><paramref name="lenientCoherency"/> (#159): when true, coherency is validated with the
        /// one-directional-lenient rule (<see cref="ValidateCoherencyNotWorsened"/>) instead of the strict
        /// end-state rule. This is behaviour-preserving for any unit that STARTS coherent (its pre-move
        /// nearest gap is &lt;= 1", so the lenient limit collapses to exactly the strict 1" and 9" limits);
        /// it only relaxes for a unit that is ALREADY broken — e.g. a mid-unit casualty left the survivors
        /// &gt;1" apart — for which it lets a hold (or any not-worse move) validate instead of throwing.
        /// See <see cref="DefinePathStage"/> for why the movement path opts in.</para>
        /// </summary>
        public static bool ValidatePaths(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors,
            IEnumerable<EnemyModelFootprint>? friendlyFootprints = null,
            bool lenientCoherency = false)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, move => budgetFor(move).MaxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            if (lenientCoherency)
                ValidateCoherencyNotWorsened(moves, ref errors);
            else
                ValidateCoherency(moves, ref errors);
            ValidateChargeReach(moves, move => budgetFor(move).MaxRushDistance, enemies, ref errors);
            ValidateEndsOnFriendly(moves, AsReadOnly(friendlyFootprints), ref errors);
            ValidateEndsOnTable(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Consolidation-move validation (#159): the cap, terrain, and enemy-crossing checks stay strict, but
        /// the coherency rule is LENIENT. A unit left out of coherency by a mid-unit casualty (its survivors
        /// end up &gt;1" apart) can't always re-form within the tiny 1-3" consolidation cap, so a consolidation
        /// is only rejected for coherency when it makes coherency WORSE than it already was (see
        /// <see cref="ValidateCoherencyNotWorsened"/>). A hold, or any move that pulls the unit together, is
        /// therefore always legal — the unit can never be trapped with no valid consolidation.
        /// </summary>
        public static bool ValidateConsolidationPaths(List<ModelMoveEntry> moves, float maxDistanceInches,
            IEnumerable<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            bool ignoresDifficultTerrain, bool ignoresImpassibleTerrain,
            IEnumerable<ITerrain>? terrain, out List<ReasonForInvalidMove> errors,
            IEnumerable<EnemyModelFootprint>? friendlyFootprints = null)
        {
            errors = new List<ReasonForInvalidMove>();

            IReadOnlyList<EnemyModelFootprint> enemies =
                enemyFootprints as IReadOnlyList<EnemyModelFootprint> ?? enemyFootprints?.ToList()
                ?? (IReadOnlyList<EnemyModelFootprint>)Array.Empty<EnemyModelFootprint>();

            ValidateOutOfMoveRange(moves, _ => maxDistanceInches, ref errors);
            ValidateMovingThroughImpassibleTerrain(moves, terrain, ignoresImpassibleTerrain, ref errors);
            ValidateMovingThroughDifficultTerrain(moves, terrain, ignoresDifficultTerrain, ref errors);
            ValidateMovingThroughEnemyUnits(moves, enemies, canMoveThroughEnemies, ref errors);
            ValidateCoherencyNotWorsened(moves, ref errors);
            ValidateEndsOnFriendly(moves, AsReadOnly(friendlyFootprints), ref errors);
            ValidateEndsOnTable(moves, ref errors);

            return errors.Count == 0;
        }

        /// <summary>
        /// Every living enemy model's base footprint (centre + radius), tagged with a per-unit key so the
        /// move-through / standoff check can tell which models belong to the same enemy unit (a charge into
        /// one model of a unit legitimately ends within the 1" standoff of that whole unit). The key is only
        /// stable within the returned list. Enemies are everyone not on the moving unit's team.
        /// </summary>
        public static List<EnemyModelFootprint> GetEnemyModelFootprints(DataBinding<UnitData> movingUnit, IGameContext gameContext)
            => GetEnemyModelFootprints(movingUnit, gameContext, excludeUnit: null);

        /// <summary>
        /// As <see cref="GetEnemyModelFootprints(DataBinding{UnitData}, IGameContext)"/>, but omits every model
        /// of <paramref name="excludeUnit"/>. Pile-in uses this to obstacle-check a defender against every enemy
        /// unit EXCEPT the one it is piling toward — so a defender stops at base contact with a third-party (or
        /// already-engaged) enemy instead of plowing through it (#159).
        /// </summary>
        public static List<EnemyModelFootprint> GetEnemyModelFootprints(DataBinding<UnitData> movingUnit,
            IGameContext gameContext, DataBinding<UnitData>? excludeUnit)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<EnemyModelFootprint> footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (ArmyData enemyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyUnit in enemyArmy.UnitBindings)
                {
                    if (excludeUnit != null && ReferenceEquals(enemyUnit.GetValue(), excludeUnit.GetValue())) continue;
                    // #207: embarked / reserve / off-table units are not obstacles. Their models are
                    // parked at the origin (EmbarkStage), so counting their footprints made (0,0) an
                    // invisible wall that rejected any legal move sweeping near the table corner.
                    if (!enemyUnit.GetValue().GetIsOnBattlefield()) continue;
                    // #029: an Aircraft can't be moved into base contact with — tag its footprints so the
                    // validator never lets a charger end engaged with it.
                    bool uncontactable = Rules.Dispatch.AircraftRules.IsAircraft(enemyUnit.GetValue());
                    bool anyLiving = false;
                    foreach (DataBinding<ModelData> enemyModel in enemyUnit.ModelBindings()
                        .Where(m => m.GetIsAlive()))
                    {
                        ModelData md = enemyModel.GetValue();
                        footprints.Add(new EnemyModelFootprint(md.PositionBinding.GetValue(), md.BaseRadiusInches,
                            unitKey, uncontactable, md.BaseShape, md.Facing));
                        anyLiving = true;
                    }
                    if (anyLiving) unitKey++;
                }
            }

            return footprints;
        }

        /// <summary>
        /// Every living on-battlefield model of a FRIENDLY unit - one on the moving unit's team, but NOT the
        /// moving unit itself. #205: a unit may pass THROUGH friendlies but may not END its move stacked on
        /// them, and only enemy footprints were validated before, so the AI resolvers happily ended on top of
        /// friendly models. Reuses <see cref="EnemyModelFootprint"/> purely as a base-footprint carrier (its
        /// UnitKey/Uncontactable fields are unused by the friendly-overlap check).
        /// </summary>
        public static List<EnemyModelFootprint> GetFriendlyModelFootprints(DataBinding<UnitData> movingUnit, IGameContext gameContext)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<EnemyModelFootprint> footprints = new List<EnemyModelFootprint>();
            int unitKey = 0;
            foreach (ArmyData friendlyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> friendlyUnit in friendlyArmy.UnitBindings)
                {
                    // Not myself - my own cohesion (not the friendly-overlap rule) governs my models' spacing.
                    if (ReferenceEquals(friendlyUnit.GetValue(), movingUnit.GetValue())) continue;
                    // Embarked / reserve / off-table units are parked at the origin and are not obstacles (#207).
                    if (!friendlyUnit.GetValue().GetIsOnBattlefield()) continue;
                    bool anyLiving = false;
                    foreach (DataBinding<ModelData> friendlyModel in friendlyUnit.ModelBindings()
                        .Where(m => m.GetIsAlive()))
                    {
                        ModelData md = friendlyModel.GetValue();
                        footprints.Add(new EnemyModelFootprint(md.PositionBinding.GetValue(), md.BaseRadiusInches,
                            unitKey, false, md.BaseShape, md.Facing));
                        anyLiving = true;
                    }
                    if (anyLiving) unitKey++;
                }
            }

            return footprints;
        }

        /// <summary>
        /// The distinct living enemy units whose footprint the move <paramref name="moves"/> pass through:
        /// any moving model's path segment comes within base-to-base contact (moving radius + enemy radius)
        /// of any living enemy model. Trigger detection for Strafing's mid-move attack. Call while the moving
        /// models are still at their START positions (the segment origin is read from each model's current
        /// position), i.e. before the move is committed.
        /// </summary>
        public static List<DataBinding<UnitData>> GetEnemyUnitsMovedThrough(
            IReadOnlyList<ModelMoveEntry> moves, DataBinding<UnitData> movingUnit, IGameContext gameContext)
        {
            PlayerID owner = movingUnit.GetValue().PlayerID;

            TeamData? ownerTeam = gameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            List<DataBinding<UnitData>> crossed = new List<DataBinding<UnitData>>();
            foreach (ArmyData enemyArmy in gameContext.GameDataStore().GetAllValues<ArmyData>()
                .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyUnit in enemyArmy.UnitBindings)
                {
                    if (enemyUnit.GetValue().GetIsDead()) continue;
                    // #207: same origin-parking gap as GetEnemyModelFootprints - an embarked unit's
                    // models sit at (0,0) and must not register as moved-through.
                    if (!enemyUnit.GetValue().GetIsOnBattlefield()) continue;
                    if (PathPassesThroughUnit(moves, enemyUnit))
                    {
                        crossed.Add(enemyUnit);
                    }
                }
            }

            return crossed;
        }

        private static bool PathPassesThroughUnit(IReadOnlyList<ModelMoveEntry> moves, DataBinding<UnitData> enemyUnit)
        {
            foreach (DataBinding<ModelData> enemyModel in enemyUnit.GetValue().ModelBindings)
            {
                if (!enemyModel.GetIsAlive()) continue;

                ModelData enemy = enemyModel.GetValue();
                // The enemy's true footprint as a zone; the moving base is swept along each path segment against
                // it (#150). For circular bases this reduces to the old combined-radius swept-disc, unchanged.
                IZone enemyZone = enemy.BaseShape.ToZone(enemy.PositionBinding.GetValue(), enemy.Facing);

                foreach (ModelMoveEntry move in moves)
                {
                    if (move.Positions.Count == 0) continue;

                    ModelData movingModel = move.Model.GetValue();
                    IBaseShape movingShape = movingModel.BaseShape;
                    Float2 movingFacing = movingModel.Facing;
                    Position segStartPos = movingModel.PositionBinding.GetValue();
                    Float2 segmentStart = new Float2(segStartPos.x, segStartPos.z);

                    foreach (Position step in move.Positions)
                    {
                        Float2 segmentEnd = new Float2(step.x, step.z);
                        if (SweptBaseGeometry.DoesSweptBaseIntersectZone(enemyZone, segmentStart, segmentEnd, movingShape, movingFacing))
                        {
                            return true;
                        }
                        segmentStart = segmentEnd;
                    }
                }
            }

            return false;
        }

        public static void ValidateChargeReach(List<ModelMoveEntry> moves, float maxRushDistance,
            IEnumerable<EnemyModelFootprint> enemyFootprints, ref List<ReasonForInvalidMove> errors)
            => ValidateChargeReach(moves, _ => maxRushDistance, enemyFootprints, ref errors);

        private static void ValidateChargeReach(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, float> maxRushDistanceFor,
            IEnumerable<EnemyModelFootprint> enemyFootprints, ref List<ReasonForInvalidMove> errors)
        {
            Dictionary<ModelMoveEntry, float> totalDistances = GetTotalMoveDistances(moves);

            //If nobody exceeds their own Rush cap, the rule doesn't apply.
            bool anyBeyondRush = totalDistances.Any(kvp => kvp.Value > maxRushDistanceFor(kvp.Key) + 0.0001f);
            if (!anyBeyondRush) return;

            //At least one model in the unit must end within melee range of an enemy model (horizontal).
            //#029: an Aircraft can't be charged, so reaching it doesn't justify a beyond-Rush charge move.
            //#312: BASE-to-base, like every other melee-range gate (MeleeRangeUtilities, GetCanCharge, the
            //GUI charge line). This used to be centre-to-centre, which demanded a base gap of
            //2" - rMine - rEnemy: merely tight for small round bases, but mathematically impossible for a
            //large base - a titan in literal base contact still read "no model ends within 2"" and the
            //charge move was rejected. All pairs are checked (never nearest-by-centre: for a rectangle the
            //nearest CENTRE is often not the nearest BASE), at the mover's END position and END facing.
            List<EnemyModelFootprint> enemies = enemyFootprints?.Where(f => !f.Uncontactable).ToList()
                ?? new List<EnemyModelFootprint>();
            float meleeRange = GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL;

            bool anyInMelee = moves.Any(move =>
            {
                ModelData model = move.Model.GetValue();
                Position end = move.Positions.Count == 0
                    ? model.PositionBinding.GetValue()
                    : move.Positions[move.Positions.Count - 1];
                Float2 endFacing = move.Positions.Count == 0 ? model.Facing : EndFacing(move, model);

                return enemies.Any(e => BaseShapeGeometry.SurfaceGap2D(
                    model.BaseShape, end, endFacing, e.BaseShape, e.Center, e.Facing) <= meleeRange + 0.0001f);
            });

            if (!anyInMelee)
            {
                //Attach the violation to the first model that went beyond its own Rush.
                ModelMoveEntry culprit = totalDistances.First(kvp => kvp.Value > maxRushDistanceFor(kvp.Key) + 0.0001f).Key;
                errors.Add(new ReasonForInvalidMove(EErrorReasonType.ChargeRangeRequiresMeleeReach, culprit.Model));
            }
        }

        private static Dictionary<ModelMoveEntry, float> GetTotalMoveDistances(List<ModelMoveEntry> moves)
        {
            Dictionary<ModelMoveEntry, float> distances = new Dictionary<ModelMoveEntry, float>();

            foreach (ModelMoveEntry modelEntry in moves)
            {
                distances.Add(modelEntry, GetTotalMoveDistance(modelEntry));
            }

            return distances;
        }

        private static void ValidateOutOfMoveRange(List<ModelMoveEntry> moves,
            Func<ModelMoveEntry, float> maxDistanceFor, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            Dictionary<ModelMoveEntry, float> totalMoveDistances = GetTotalMoveDistances(moves);

            foreach (KeyValuePair<ModelMoveEntry, float> kvp in totalMoveDistances)
            {
                if (kvp.Value > maxDistanceFor(kvp.Key))
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.OutOfMoveRange, kvp.Key.Model));
                }
            }
        }

        private static void ValidateMovingThroughImpassibleTerrain(List<ModelMoveEntry> moves,
            IEnumerable<ITerrain>? terrain, bool ignoresImpassibleTerrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;
            // Flying (AllTerrain scope) flies over impassible terrain — its path may cross an impassible piece
            // (it still can't end stacked on enemies; coherency + the standoff checks remain in force).
            if (ignoresImpassibleTerrain) return;

            //Snapshot impassable pieces so each model walk doesn't re-enumerate.
            List<ITerrain> impassable = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();
            if (impassable.Count == 0) return;

            // #341: the authoritative gate and the preview's "show me why" finder are now ONE walk. They were
            // two copies of the same segment loop that docs/ResolverGuide.md requires never to diverge, and the
            // two-attitude leg rule plus the per-node pose check gave them far more to keep in step.
            foreach (ModelMoveEntry move in moves)
            {
                if (FindFirstTerrainCrossing(move, impassable, ELegAttitudeRule.EitherAttitudeClears) != null)
                {
                    reasonsForInvalidMove.Add(
                        new ReasonForInvalidMove(EErrorReasonType.MovingThroughImpassibleTerrain, move.Model));
                }
            }
        }

        // True when a path segment has effectively no length (a held / stationary step). Such a step can't
        // move a model "through" terrain, so the terrain validators skip it (see the impassible-terrain note).
        private const float ZERO_MOVE_EPSILON_SQ = 1e-8f;
        private static bool IsZeroLengthSegment(Float2 a, Float2 b)
        {
            float dx = b.X - a.X, dz = b.Y - a.Y;
            return dx * dx + dz * dz < ZERO_MOVE_EPSILON_SQ;
        }

        // The base's orientation as it ARRIVES at Positions[i]: the move's per-waypoint travel facing
        // (#150, Facings[i]), the same oriented base the ghost drew and the executor will turn the model to.
        // Falls back to the model's pre-move resting facing when the move carries no per-waypoint facings
        // (AI moves, consolidation, aircraft - Facings is null).
        private static Float2 ArriveFacing(ModelMoveEntry move, int i, Float2 resting)
            => move.Facings != null && i < move.Facings.Count ? move.Facings[i] : resting;

        // #341: the orientation the base DEPARTED the previous node with - the facing that node was placed at,
        // or the model's pre-move resting facing for the first leg. A leg runs between two attitudes, and this
        // is the one the old single-facing sweep silently threw away: it swept the whole leg at the ARRIVING
        // attitude, so a rotation the player dialled in for the node they were placing was applied to the
        // ground they were standing on. A rectangle parked beside a wall could not be told to turn at all.
        private static Float2 DepartFacing(ModelMoveEntry move, int i, Float2 resting)
            => i == 0 ? resting : ArriveFacing(move, i - 1, resting);

        // Two attitudes that are the same heading - the overwhelmingly common case (no manual rotation, or a
        // straight leg), where the two-attitude leg rule collapses back to a single sweep and costs nothing.
        private const float FACING_EPSILON = 1e-4f;
        private static bool FacingsEqual(Float2 a, Float2 b)
            => MathF.Abs(a.X - b.X) < FACING_EPSILON && MathF.Abs(a.Y - b.Y) < FACING_EPSILON;

        /// <summary>
        /// #341 - how a leg whose two endpoint attitudes DISAGREE is treated. A leg's rotation is deliberately
        /// left unvalidated (the base turns from the node it left to the node it arrives at somewhere along the
        /// way; the animation decides when), so the two questions a swept test can be asked need opposite
        /// answers and each caller has to say which it means.
        /// </summary>
        private enum ELegAttitudeRule
        {
            /// <summary>
            /// Hazard detection (Dangerous / Difficult): the ARRIVING attitude alone, exactly as before #341.
            /// "Does this ground affect the model" is not "is this move legal", and widening it either way
            /// would change how often units take terrain wounds or hit the 6" cap.
            /// </summary>
            ArrivingAttitude,

            /// <summary>
            /// Legality (Impassible): the leg is blocked only when the swept footprint collides at BOTH
            /// endpoint attitudes - one clear attitude is enough, because the model could have been holding it
            /// for the whole leg. The OR is evaluated over the WHOLE obstacle set per attitude ("there is one
            /// attitude at which this leg is clear of everything"), never per obstacle. Each node's pose is
            /// then checked strictly on its own, which is what still stops a move ending rotated into a wall.
            /// </summary>
            EitherAttitudeClears,
        }

        // The base's orientation at the END of the move: the last per-waypoint facing (#282 - committed
        // waypoints keep the facing they were placed with, and the executor turns the model to it), falling
        // back to the resting facing for moves that carry none (AI, consolidation holds). Every END-state
        // check must measure the base at this orientation, not the pre-move one - for a rectangle the two
        // can differ by the full inscribed-vs-circumscribed spread (#312).
        private static Float2 EndFacing(ModelMoveEntry move, ModelData model)
            => move.Facings != null && move.Facings.Count > 0
                ? move.Facings[move.Facings.Count - 1]
                : model.Facing;

        public static bool DoesPathCrossDangerousTerrain(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => DoesPathCrossTerrainPieces(move, terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList());

        /// <summary>Public sibling of the Dangerous check (#155): whether the move's swept path touches any
        /// Difficult-flagged piece. Lets a move-preview resolver know a model's committed waypoints have
        /// already crossed Difficult terrain, so the rest of its move is subject to
        /// <see cref="GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/> (see
        /// <see cref="ClampTravelForDifficultTerrain"/>).</summary>
        public static bool DoesPathCrossDifficultTerrain(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => DoesPathCrossTerrainPieces(move, terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList());

        /// <summary>Public sibling of the Difficult/Dangerous checks (#213): whether the move's swept base
        /// path touches any Impassible-flagged piece. Lets a move-preview resolver flag a "moving through
        /// impassible terrain" path as invalid (red, un-clickable) up front, instead of only rejecting it at
        /// the Done gate. The authoritative enforcement is <see cref="ValidateMovingThroughImpassibleTerrain"/>;
        /// this is the same swept-base test it uses, for preview.</summary>
        public static bool DoesPathCrossImpassibleTerrain(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => FindFirstImpassibleCrossing(move, terrain) != null;

        /// <summary>Where a flagged path actually collides, for the preview's "show me why" feedback: the
        /// terrain piece, the path segment (by index), the sweep's oriented facing, and the model's centre at
        /// first contact. A collision is often nowhere near the node being placed — a pivot at an earlier
        /// waypoint can make a leg the player already placed collide — so "invalid" alone reads as an
        /// inexplicable red flag on open ground.
        /// <para>#341: it may also be a NODE POSE rather than a leg, reported as a zero-length segment at
        /// that node (<see cref="SegmentStart"/> == <see cref="SegmentEnd"/>). The move has room to travel
        /// there; what it has no room for is standing there at that attitude.</para></summary>
        public readonly struct TerrainCrossing
        {
            public readonly ITerrain Piece;
            /// <summary>Index into <see cref="ModelMoveEntry.Positions"/> of the segment that collides (its
            /// start is the previous waypoint, or the model's current position for index 0).</summary>
            public readonly int SegmentIndex;
            public readonly Float2 SegmentStart;
            public readonly Float2 SegmentEnd;
            /// <summary>The travel facing the swept base used for this segment (offset-adjusted, #150).</summary>
            public readonly Float2 Facing;
            /// <summary>The model's centre when its swept footprint first touches the piece — the segment
            /// start itself when the re-oriented base already overlaps before travelling.</summary>
            public readonly Float2 ContactCentre;

            public TerrainCrossing(ITerrain piece, int segmentIndex, Float2 segmentStart, Float2 segmentEnd,
                Float2 facing, Float2 contactCentre)
            {
                Piece = piece; SegmentIndex = segmentIndex; SegmentStart = segmentStart; SegmentEnd = segmentEnd;
                Facing = facing; ContactCentre = contactCentre;
            }
        }

        /// <summary>Detailed sibling of <see cref="DoesPathCrossImpassibleTerrain"/>: the first impassible
        /// collision along the path (walking segments in order), or null when the path is clear. Same swept-base
        /// test, so it flags exactly when the boolean does.</summary>
        public static TerrainCrossing? FindFirstImpassibleCrossing(ModelMoveEntry move, IEnumerable<ITerrain> terrain)
            => FindFirstTerrainCrossing(move, terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList(), ELegAttitudeRule.EitherAttitudeClears);

        private static bool DoesPathCrossTerrainPieces(ModelMoveEntry move, List<ITerrain> pieces)
            => FindFirstTerrainCrossing(move, pieces, ELegAttitudeRule.ArrivingAttitude) != null;

        /// <summary>
        /// Walks a path against a set of terrain pieces and reports the first collision, under the chosen
        /// <see cref="ELegAttitudeRule"/> (#341). Two kinds of collision can be reported:
        /// <list type="bullet">
        /// <item>a LEG - the swept footprint between two nodes; and</item>
        /// <item>a NODE POSE - the static footprint at a waypoint, at the facing that waypoint was placed
        /// with. Only under <see cref="ELegAttitudeRule.EitherAttitudeClears"/>, where it is the whole point:
        /// the leg rule accepts a leg that is clear at the DEPARTING attitude, so without this a move could
        /// end (or turn a corner) rotated into a wall and nothing would catch it. Reported as a zero-length
        /// segment at the node, which is exactly what it is.</item>
        /// </list>
        /// A pose identical to the one before it is skipped: it is not a pose this move creates, and flagging
        /// it would self-flag every hold by a model already overlapping a piece - the AI's hold-in-place
        /// fallback, which is the documented reason zero-length legs are skipped too.
        /// </summary>
        private static TerrainCrossing? FindFirstTerrainCrossing(ModelMoveEntry move, List<ITerrain> pieces,
            ELegAttitudeRule attitudeRule)
        {
            if (pieces.Count == 0 || move.Positions.Count == 0) return null;

            var model = move.Model.GetValue();
            IBaseShape baseShape = model.BaseShape;
            Float2 restingFacing = model.Facing;
            Position startPos = model.PositionBinding.GetValue();
            Float2 segmentStart = new Float2(startPos.x, startPos.z);
            bool eitherClears = attitudeRule == ELegAttitudeRule.EitherAttitudeClears;

            for (int i = 0; i < move.Positions.Count; i++)
            {
                Float2 segmentEnd = new Float2(move.Positions[i].x, move.Positions[i].z);
                Float2 arriveFacing = ArriveFacing(move, i, restingFacing);
                Float2 departFacing = DepartFacing(move, i, restingFacing);
                bool held = IsZeroLengthSegment(segmentStart, segmentEnd); // a hold doesn't cross terrain

                if (!held)
                {
                    // Sweep the model's true base footprint (shape + facing) along the leg so base overlap -
                    // not just the centre crossing - counts as moving through the piece (#150).
                    TerrainCrossing? leg = FirstSweptCollision(pieces, segmentStart, segmentEnd, baseShape,
                        arriveFacing, i);
                    // The arriving attitude is tried first: it is the one the ghost drew, and on a leg that is
                    // clear at it (the common case) the second sweep never runs.
                    if (leg != null && (!eitherClears
                                        || FacingsEqual(departFacing, arriveFacing)
                                        || AnySweptCollision(pieces, segmentStart, segmentEnd, baseShape, departFacing)))
                        return leg;
                }

                if (eitherClears && !(held && FacingsEqual(departFacing, arriveFacing)))
                {
                    TerrainCrossing? pose = FirstSweptCollision(pieces, segmentEnd, segmentEnd, baseShape,
                        arriveFacing, i);
                    if (pose != null) return pose;
                }

                segmentStart = segmentEnd;
            }

            return null;
        }

        // The first piece the swept base touches, as a reportable crossing (null when clear). A zero-length
        // sweep is a static pose test, and reports the node itself as the contact centre.
        private static TerrainCrossing? FirstSweptCollision(List<ITerrain> pieces, Float2 from, Float2 to,
            IBaseShape baseShape, Float2 facing, int segmentIndex)
        {
            foreach (ITerrain piece in pieces)
            {
                if (!SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, from, to, baseShape, facing))
                    continue;
                // The centre at first touch: known-clear travel along the segment (0 when the base
                // already overlaps at the segment start, and for a node pose, which has nowhere to travel).
                float entry = SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                    piece.Shape, from, to, baseShape, facing);
                float dx = to.X - from.X, dz = to.Y - from.Y;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                Float2 contact = len > 0f
                    ? new Float2(from.X + dx / len * entry, from.Y + dz / len * entry)
                    : from;
                return new TerrainCrossing(piece, segmentIndex, from, to, facing, contact);
            }
            return null;
        }

        // The same question without the reporting work, for the second attitude of the #341 leg rule.
        private static bool AnySweptCollision(List<ITerrain> pieces, Float2 from, Float2 to,
            IBaseShape baseShape, Float2 facing)
        {
            foreach (ITerrain piece in pieces)
                if (SweptBaseGeometry.DoesSweptBaseIntersectZone(piece.Shape, from, to, baseShape, facing))
                    return true;
            return false;
        }

        /// <summary>Safety margin the difficult-terrain preview clamp keeps below the exact limits — both the
        /// 6" cap and the stop-short-of-the-edge distance — so a clamped endpoint re-validated with exact
        /// constants can't fail on float drift (#155).</summary>
        public const float DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES = 0.01f;

        /// <summary>Why (and whether) <see cref="ClampTravelForDifficultTerrainDetailed"/> shortened a segment
        /// (#155) — lets a resolver show the right on-screen warning.</summary>
        public enum EDifficultClampKind
        {
            /// <summary>Difficult terrain didn't constrain this segment.</summary>
            NotLimited,
            /// <summary>The model moves through Difficult terrain, so its total move is held to the 6" cap.</summary>
            CappedCrossing,
            /// <summary>The model had already moved too far to afford entering, so it stops short of the edge.</summary>
            StoppedShortOfEdge,
        }

        /// <summary>Result of the difficult-terrain preview clamp: how far the segment may travel, and why it
        /// was (or wasn't) shortened (#155).</summary>
        public readonly struct DifficultClampResult
        {
            public readonly float AllowedInches;
            public readonly EDifficultClampKind Kind;
            public DifficultClampResult(float allowedInches, EDifficultClampKind kind)
            { AllowedInches = allowedInches; Kind = kind; }
        }

        /// <summary>
        /// How far a model may actually travel along one straight preview segment, honouring the
        /// difficult-terrain move cap (#155) — the enforcement mirror of
        /// <see cref="ValidateMovingThroughDifficultTerrain"/>, for resolvers that clamp a live ghost instead
        /// of reporting an error after the fact. Band/charge caps are the caller's job — the returned distance
        /// only ever shrinks the segment. See <see cref="ClampTravelForDifficultTerrainDetailed"/> for the
        /// per-case reason.
        /// </summary>
        public static float ClampTravelForDifficultTerrain(Float2 segmentStart, Float2 segmentEnd,
            float traveledBeforeSegmentInches, bool pathAlreadyCrossedDifficultTerrain,
            IBaseShape baseShape, Float2 facing,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain)
            => ClampTravelForDifficultTerrainDetailed(segmentStart, segmentEnd, traveledBeforeSegmentInches,
                pathAlreadyCrossedDifficultTerrain, baseShape, facing, terrain, ignoresDifficultTerrain).AllowedInches;

        /// <summary>
        /// As <see cref="ClampTravelForDifficultTerrain"/>, but also reports WHY the segment was shortened so a
        /// resolver can warn accordingly (#155). Cases:
        /// <list type="bullet">
        /// <item>No Difficult terrain in play, a zero-length segment, or
        /// <paramref name="ignoresDifficultTerrain"/> (Strider/Flying): full segment,
        /// <see cref="EDifficultClampKind.NotLimited"/>.</item>
        /// <item>The path so far has already crossed Difficult terrain
        /// (<paramref name="pathAlreadyCrossedDifficultTerrain"/>) OR this segment enters it with cap room to
        /// spare: the whole move is capped at
        /// <see cref="GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES"/> total —
        /// <see cref="EDifficultClampKind.CappedCrossing"/>.</item>
        /// <item>The segment would enter Difficult terrain but the model has already moved too far to afford
        /// entering: it stops just short of the edge — <see cref="EDifficultClampKind.StoppedShortOfEdge"/>.</item>
        /// </list>
        /// </summary>
        public static DifficultClampResult ClampTravelForDifficultTerrainDetailed(Float2 segmentStart, Float2 segmentEnd,
            float traveledBeforeSegmentInches, bool pathAlreadyCrossedDifficultTerrain,
            IBaseShape baseShape, Float2 facing,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain)
        {
            float dx = segmentEnd.X - segmentStart.X, dz = segmentEnd.Y - segmentStart.Y;
            float desired = MathF.Sqrt(dx * dx + dz * dz);
            if (ignoresDifficultTerrain || desired <= 0f || terrain == null)
                return new DifficultClampResult(desired, EDifficultClampKind.NotLimited);

            List<ITerrain> difficult = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList();
            if (difficult.Count == 0) return new DifficultClampResult(desired, EDifficultClampKind.NotLimited);

            float capRemaining = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES
                - traveledBeforeSegmentInches - DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES;

            if (pathAlreadyCrossedDifficultTerrain)
                return new DifficultClampResult(Math.Clamp(capRemaining, 0f, desired), EDifficultClampKind.CappedCrossing);

            // Farthest travel along the segment before the swept base first touches any difficult piece.
            float entry = desired;
            foreach (ITerrain piece in difficult)
            {
                entry = MathF.Min(entry, SweptBaseGeometry.MaxTravelBeforeZoneIntersection(
                    piece.Shape, segmentStart, segmentEnd, baseShape, facing));
                if (entry <= 0f) break;
            }
            if (entry >= desired) return new DifficultClampResult(desired, EDifficultClampKind.NotLimited); // never enters

            if (capRemaining > entry)
                return new DifficultClampResult(MathF.Min(desired, capRemaining), EDifficultClampKind.CappedCrossing);
            return new DifficultClampResult(
                Math.Clamp(entry - DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES, 0f, desired),
                EDifficultClampKind.StoppedShortOfEdge);
        }

        private static void ValidateMovingThroughDifficultTerrain(List<ModelMoveEntry> moves,
            IEnumerable<ITerrain>? terrain, bool ignoresDifficultTerrain, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (terrain == null) return;
            // Strider (and a future Flying rule) waive the difficult-terrain move cap entirely — the unit
            // may cross Difficult terrain without its move being limited to DIFFICULT_TERRAIN_MOVE_CAP_INCHES.
            if (ignoresDifficultTerrain) return;

            List<ITerrain> difficult = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult))
                .ToList();
            if (difficult.Count == 0) return;

            Dictionary<ModelMoveEntry, float> distances = GetTotalMoveDistances(moves);

            foreach (ModelMoveEntry move in moves)
            {
                if (DoesPathCrossTerrainPieces(move, difficult)
                    && distances[move] > GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)
                {
                    reasonsForInvalidMove.Add(
                        new ReasonForInvalidMove(EErrorReasonType.ExceededDifficultTerrainMoveLimit, move.Model));
                }
            }
        }

        // Float slack so a charge that lands exactly base-to-base isn't rejected by sub-thousandth rounding.
        private const float ENEMY_PROXIMITY_EPSILON_INCHES = 0.001f;

        // A move ending within this base-to-base gap of an enemy counts as "in base contact" (a charge),
        // which is legal; the forbidden standoff band is (this, ENEMY_STANDOFF_DISTANCE_INCHES). Generous
        // enough to absorb click/float imprecision when a player drags a charger up to touch an enemy.
        private const float ENEMY_CONTACT_TOLERANCE_INCHES = 0.1f;

        /// <summary>
        /// A model may not move <i>through</i> an enemy base — its swept base may not overlap an enemy base
        /// mid-path, nor end stacked on one. Only moves that actually close the distance are penalised, so a
        /// model already inside an enemy's reach (e.g. left there by a pile-in/consolidation move) can still
        /// move away or hold without being trapped into an impossible-to-satisfy state.
        ///
        /// #206 — there is deliberately NO "must end 1in from an enemy without charging" standoff rejection
        /// here. A non-charge move MAY end inside the standoff band (right up against an enemy); the
        /// consequence is enforced downstream, not at move time — <see cref="ChooseActionStage.GetCanPass"/>
        /// gates Pass when the unit ends within <see cref="GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES"/>
        /// of an enemy, so the unit is forced to Charge (or reposition) rather than blocked from finishing its
        /// move. Uncontactable enemies (Aircraft, #029) keep their own standoff below: they can't be charged,
        /// so closing into contact with one has no legal follow-up and stays rejected.
        /// </summary>
        private static void ValidateMovingThroughEnemyUnits(List<ModelMoveEntry> moves,
            IReadOnlyList<EnemyModelFootprint> enemyFootprints, bool canMoveThroughEnemies,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (enemyFootprints == null || enemyFootprints.Count == 0) return;

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                var movingModel = move.Model.GetValue();
                IBaseShape movingShape = movingModel.BaseShape;
                Float2 restingFacing = movingModel.Facing;
                Float2 endFacing = EndFacing(move, movingModel);
                Position start = movingModel.PositionBinding.GetValue();
                Position end = move.Positions[move.Positions.Count - 1];

                bool flaggedThrough = false;
                bool flaggedStandoff = false;

                foreach (EnemyModelFootprint enemy in enemyFootprints)
                {
                    // Start/end base-to-base gaps use the true, facing-oriented footprints (#150); for circular
                    // bases this is exactly the old `distance − (rMoving + rEnemy)`, so circle behaviour is
                    // unchanged, and a rotated rectangular base measures by its real outline. The start gap
                    // measures the base as it RESTS pre-move; the end gap measures it at the facing the
                    // executor will actually leave it at (#312 - these can differ by the full width-vs-length
                    // spread of a rectangle).
                    float startGap = BaseShapeGeometry.SurfaceGap2D(movingShape, start, restingFacing, enemy.BaseShape, enemy.Center, enemy.Facing);
                    float endGap = BaseShapeGeometry.SurfaceGap2D(movingShape, end, endFacing, enemy.BaseShape, enemy.Center, enemy.Facing);
                    bool movedCloser = endGap < startGap - ENEMY_PROXIMITY_EPSILON_INCHES;

                    // #029: an Aircraft can't be moved into base contact with — a move that closes to within the
                    // standoff distance of it (the base-contact zone OR the standoff band) is rejected, so it can
                    // never be charged or stacked on. Units may still pass UNDER it (the through-check is skipped),
                    // and a unit it flew adjacent to isn't trapped (only moves that close the gap are penalised).
                    if (enemy.Uncontactable)
                    {
                        if (!flaggedStandoff && movedCloser
                            && endGap < GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES - ENEMY_PROXIMITY_EPSILON_INCHES)
                        {
                            reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.EndedTooCloseToEnemy, move.Model));
                            flaggedStandoff = true;
                        }
                        continue;
                    }

                    //Pass-through (#150, shape- and facing-aware): the base starts CLEAR of this enemy and ends
                    //CLEAR of it, yet its swept footprint crosses the enemy's base somewhere along the path — so
                    //it must have gone in one side and out the other. The same swept-zone test the Strafing
                    //through-check uses (exact for rectangles at any facing; a circle reduces to the old swept
                    //disc). A clean charge ends in CONTACT (endGap ≈ 0), so it's not "ends clear" and is handled
                    //by the ending-stacked / standoff rules below, not here. A model that begins in contact
                    //(startGap ≤ tol) isn't newly penalised for its pre-existing position. canMoveThroughEnemies
                    //(Strafing fly-over) is exempt — it may path through an enemy base.
                    if (!canMoveThroughEnemies && !flaggedThrough
                        && startGap > ENEMY_CONTACT_TOLERANCE_INCHES && endGap > ENEMY_CONTACT_TOLERANCE_INCHES)
                    {
                        IZone enemyZone = enemy.BaseShape.ToZone(enemy.Center, enemy.Facing);
                        Position segStart = start;
                        for (int i = 0; i < move.Positions.Count; i++)
                        {
                            Position step = move.Positions[i];
                            Float2 a = new Float2(segStart.x, segStart.z);
                            Float2 b = new Float2(step.x, step.z);
                            // #312: sweep each segment at its travel facing (the orientation the ghost drew and
                            // the executor applies), mirroring the terrain validators' 2026-07-25 fix.
                            // #341: but a leg runs BETWEEN two attitudes, and its rotation is not validated - so
                            // the leg is "through" this enemy only when it crosses at both of them. Sweeping the
                            // arriving attitude alone applied a turn the player dialled in for the node being
                            // placed to the ground the model set off from. (Scoped per enemy, like every other
                            // clause of this check - the question here is "did I pass through THIS enemy".)
                            Float2 arriveFacing = ArriveFacing(move, i, restingFacing);
                            Float2 departFacing = DepartFacing(move, i, restingFacing);
                            if (SweptBaseGeometry.DoesSweptBaseIntersectZone(enemyZone, a, b, movingShape, arriveFacing)
                                && (FacingsEqual(departFacing, arriveFacing)
                                    || SweptBaseGeometry.DoesSweptBaseIntersectZone(enemyZone, a, b, movingShape, departFacing)))
                            {
                                reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.MovingThroughEnemyUnit, move.Model));
                                flaggedThrough = true;
                                break;
                            }
                            segStart = step;
                        }
                    }

                    //Ending stacked on an enemy is never allowed, even charging.
                    if (!flaggedThrough && endGap < -ENEMY_CONTACT_TOLERANCE_INCHES && movedCloser)
                    {
                        reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.MovingThroughEnemyUnit, move.Model));
                        flaggedThrough = true;
                    }

                    // #206 — no standoff-band rejection for a contactable enemy: a non-charge move may end right
                    // up against one. GetCanPass forces the charge afterward. (The aircraft branch above keeps
                    // its own standoff — an Aircraft can't be charged, so contact with it has no follow-up.)

                    if (flaggedThrough) break;
                }
            }
        }

        /// <summary>
        /// #205: a model may not END its move stacked on top of a FRIENDLY unit's base. Passing THROUGH a
        /// friendly is legal (only the end position is checked, never the swept path), and a model that was
        /// ALREADY overlapping a friendly at its start isn't newly penalised (mirrors the enemy rule's "only
        /// moves that close the distance") - so a unit is never trapped with no legal move. Friendlies have no
        /// standoff band: only true base overlap (interpenetration, not mere contact) is illegal, which is why
        /// this uses <see cref="BaseShapeGeometry.AreColliding"/> (SurfaceGap &lt; 0), not the enemy standoff.
        /// The GUI resolver enforces the same rule live (WouldOverlapAnyModel); this is the authoritative
        /// engine guard the AI resolvers were missing.
        /// </summary>
        private static void ValidateEndsOnFriendly(List<ModelMoveEntry> moves,
            IReadOnlyList<EnemyModelFootprint> friendlyFootprints, ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            if (friendlyFootprints == null || friendlyFootprints.Count == 0) return;

            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                ModelData movingModel = move.Model.GetValue();
                IBaseShape movingShape = movingModel.BaseShape;
                Float2 restingFacing = movingModel.Facing;
                Float2 endFacing = EndFacing(move, movingModel);
                Position start = movingModel.PositionBinding.GetValue();
                Position end = move.Positions[move.Positions.Count - 1];

                foreach (EnemyModelFootprint friendly in friendlyFootprints)
                {
                    // #312: the end state is measured at the END facing (the base the executor leaves behind),
                    // the start state at the resting facing the base actually stood at.
                    if (!BaseShapeGeometry.AreColliding(movingShape, end, endFacing,
                            friendly.BaseShape, friendly.Center, friendly.Facing))
                        continue;

                    // Already overlapping this friendly at the start (should never happen in legal play) - don't
                    // trap the unit by rejecting a move it can't avoid; only NEWLY ending stacked is illegal.
                    if (BaseShapeGeometry.AreColliding(movingShape, start, restingFacing,
                            friendly.BaseShape, friendly.Center, friendly.Facing))
                        continue;

                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.EndedOnFriendlyUnit, move.Model));
                    break; // one flag per model is enough
                }
            }
        }

        // #291 — float slack on the table edge, so a model deliberately parked flush against it isn't
        // rejected by sub-thousandth rounding in the footprint corners.
        private const float TABLE_EDGE_EPSILON_INCHES = 0.001f;

        /// <summary>
        /// #291 — no model may end a move with any part of its base off the table. Measured against the
        /// model's TRUE oriented footprint (corners plus the Minkowski rounding), not a bounding circle:
        /// a vehicle on a 4"x2" rectangular base must be able to sit flush along an edge with its long
        /// side parallel to it, which a circumscribing-radius test would forbid by over an inch.
        ///
        /// <para>This was missing entirely — the movement validator had no table-bounds rule at all, and
        /// the only thing keeping models on the board was the GUI refusing clicks outside it, which
        /// constrains a model's CENTRE. That is why the symptom showed up on vehicles: a big base
        /// overhangs the edge long before its centre leaves the table.</para>
        ///
        /// <para>Like <see cref="ValidateEndsOnFriendly"/> and <see cref="ValidateCoherencyNotWorsened"/>,
        /// this is a "not worsened" rule rather than an absolute one. A model that somehow already
        /// overhangs (an older save, a forced move, a future bug) must not be frozen in place by a
        /// validator that rejects every move it can make — a move that reduces the overhang, or removes
        /// it, is always legal. In normal play nothing starts off the table and the rule is simply
        /// "stay on the board".</para>
        /// </summary>
        private static void ValidateEndsOnTable(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            foreach (ModelMoveEntry move in moves)
            {
                if (move.Positions.Count == 0) continue;

                ModelData model = move.Model.GetValue();
                Position end = move.Positions[move.Positions.Count - 1];
                // #282: a path carries a facing per waypoint; the end facing is the one the base rests at.
                Float2 endFacing = EndFacing(move, model);

                float endOverhang = OverhangInches(model.BaseShape, end, endFacing);
                if (endOverhang <= TABLE_EDGE_EPSILON_INCHES) continue;

                float startOverhang = OverhangInches(model.BaseShape,
                    model.PositionBinding.GetValue(), model.Facing);
                if (endOverhang <= startOverhang + TABLE_EDGE_EPSILON_INCHES) continue;

                reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.EndedOffTable, move.Model));
            }
        }

        /// <summary>
        /// How far the furthest part of <paramref name="shape"/> - placed at <paramref name="centre"/>
        /// facing <paramref name="facing"/> - sticks out past the table bounds, in inches. 0 when the whole
        /// base is on the table. Internal for tests.
        /// </summary>
        internal static float OverhangInches(IBaseShape shape, Position centre, Float2 facing)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

            BaseFootprint footprint = shape.Footprint(centre, facing);
            float worst = 0f;
            foreach (Float2 corner in footprint.Corners)
            {
                // The rounding radius inflates every hull corner, so the extreme point on each axis is the
                // corner plus (or minus) the rounding. A circle is one corner rounded by its radius.
                // Footprint corners are world-space (X, Y) = the table's (x, z) plane.
                worst = MathF.Max(worst, footprint.Rounding - corner.X);          // past the left edge
                worst = MathF.Max(worst, corner.X + footprint.Rounding - w);      // past the right edge
                worst = MathF.Max(worst, footprint.Rounding - corner.Y);          // past the near edge
                worst = MathF.Max(worst, corner.Y + footprint.Rounding - h);      // past the far edge
            }
            return worst;
        }

        /// <summary>
        /// #291 — the furthest a model may travel along (<paramref name="dirX"/>, <paramref name="dirZ"/>)
        /// from <paramref name="from"/> without ending further off the table than it started, capped at
        /// <paramref name="allowedInches"/>. The move resolvers call this so the ghost stops AT the edge
        /// instead of proposing a path <see cref="ValidateEndsOnTable"/> would reject — the same
        /// "the preview can never propose an invalid move" discipline the terrain and enemy clamps follow.
        ///
        /// <para>Solved by bisection rather than analytically: it has to hold for any
        /// <see cref="IBaseShape"/> at any facing, and the predicate (<see cref="OverhangInches"/>) is
        /// cheap enough to sample. Direction is assumed normalised.</para>
        /// </summary>
        public static float ClampTravelToTable(Position from, float dirX, float dirZ, float allowedInches,
            IBaseShape shape, Float2 facing)
        {
            if (allowedInches <= 0f) return allowedInches;

            // Matching the validator: a model that already overhangs may keep that much overhang, so the
            // budget it is measured against is its own starting state, not zero.
            float budget = OverhangInches(shape, from, facing) + TABLE_EDGE_EPSILON_INCHES;

            bool Fits(float t) => OverhangInches(shape,
                new Position(from.x + dirX * t, from.z + dirZ * t), facing) <= budget;

            if (Fits(allowedInches)) return allowedInches;

            float low = 0f, high = allowedInches;
            for (int i = 0; i < 24; i++)   // ~1e-7" on a 48" table: far finer than anything visible
            {
                float mid = (low + high) * 0.5f;
                if (Fits(mid)) low = mid; else high = mid;
            }
            return low;
        }

        // Float slack on the cohesion limits so a move that lands a model exactly on the 1"/9" boundary
        // isn't rejected by sub-thousandth rounding in the 3D base-to-base distance.
        private const float COHESION_EPSILON_INCHES = 0.001f;

        private static void ValidateCoherency(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            List<DataBinding<ModelData>> models = new List<DataBinding<ModelData>>();
            List<Position> positions = new List<Position>();
            List<Float2> facings = new List<Float2>();

            //Figure out where all the models will be after moving. Dead models are out of play — they
            //leave a hole in the formation rather than anchoring it, so a casualty's last position must
            //not count toward cohesion (it would wrongly fail the survivors for being "too far" from a corpse).
            //#312: measure each base at its END facing too - the post-move formation is what the rule is about.
            foreach (ModelMoveEntry moveEntry in moves)
            {
                if (moveEntry.Model.GetValue().GetIsAlive() == false)
                {
                    continue;
                }

                models.Add(moveEntry.Model);
                positions.Add(moveEntry.Positions.Count > 0
                    ? moveEntry.Positions.Last()
                    : moveEntry.Model.GetValue().PositionBinding.GetValue());
                facings.Add(moveEntry.Positions.Count > 0
                    ? EndFacing(moveEntry, moveEntry.Model.GetValue())
                    : moveEntry.Model.GetValue().Facing);
            }

            //If there's just one living model, there's nothing to compare.
            if (models.Count <= 1)
            {
                return;
            }

            //Check each model's distance against all the others, for both kinds of coherency.
            //As of 3.4.0, each model must be within 1" of another model and within 9" of every other model.

            ComputeCohesionExtents(models, positions, facings,
                out float[] nearestDistances, out float[] farthestDistances);

            for (int i = 0; i < models.Count; i++)
            {
                if (nearestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES + COHESION_EPSILON_INCHES)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAnyUnitModel, models[i]));
                }

                if (farthestDistances[i] > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES + COHESION_EPSILON_INCHES)
                {
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAllUnitModels, models[i]));
                }
            }
        }


        /// <summary>
        /// Lenient coherency check for consolidation (#159): tolerates a break that already existed at the
        /// models' CURRENT (pre-move) positions. A model is flagged only when the move makes its coherency
        /// worse — its post-move nearest-neighbour gap exceeds max(1", its pre-move nearest gap), or its
        /// post-move farthest gap exceeds max(9", its pre-move farthest gap). So a mid-unit casualty that
        /// leaves survivors &gt;1" apart doesn't trap the unit: a hold (positions unchanged) is always valid,
        /// and any re-forming move that shrinks the gaps is valid, but a move that scatters the unit further
        /// is still rejected. Mirrors the enemy-standoff rule's "only penalise moves that close the distance".
        /// </summary>
        private static void ValidateCoherencyNotWorsened(List<ModelMoveEntry> moves,
            ref List<ReasonForInvalidMove> reasonsForInvalidMove)
        {
            List<DataBinding<ModelData>> models = new List<DataBinding<ModelData>>();
            List<Position> before = new List<Position>();
            List<Position> after = new List<Position>();
            List<Float2> beforeFacings = new List<Float2>();
            List<Float2> afterFacings = new List<Float2>();

            foreach (ModelMoveEntry moveEntry in moves)
            {
                if (moveEntry.Model.GetValue().GetIsAlive() == false) continue;

                models.Add(moveEntry.Model);
                before.Add(moveEntry.Model.GetValue().PositionBinding.GetValue());
                after.Add(moveEntry.Positions.Count > 0
                    ? moveEntry.Positions.Last()
                    : moveEntry.Model.GetValue().PositionBinding.GetValue());
                //#312: the before-state is the base as it rests; the after-state is the base at the facing the
                //executor will leave it at.
                beforeFacings.Add(moveEntry.Model.GetValue().Facing);
                afterFacings.Add(moveEntry.Positions.Count > 0
                    ? EndFacing(moveEntry, moveEntry.Model.GetValue())
                    : moveEntry.Model.GetValue().Facing);
            }

            if (models.Count <= 1) return;

            ComputeCohesionExtents(models, before, beforeFacings, out float[] nearestBefore, out float[] farthestBefore);
            ComputeCohesionExtents(models, after, afterFacings, out float[] nearestAfter, out float[] farthestAfter);

            for (int i = 0; i < models.Count; i++)
            {
                float nearestLimit = Math.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES,
                    nearestBefore[i]) + COHESION_EPSILON_INCHES;
                if (nearestAfter[i] > nearestLimit)
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAnyUnitModel, models[i]));

                float farthestLimit = Math.Max(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES,
                    farthestBefore[i]) + COHESION_EPSILON_INCHES;
                if (farthestAfter[i] > farthestLimit)
                    reasonsForInvalidMove.Add(new ReasonForInvalidMove(EErrorReasonType.TooFarFromAllUnitModels, models[i]));
            }
        }

        // Per-model nearest- and farthest-neighbour base-to-base 3D distances at the given positions and
        // facings (both index-aligned with <paramref name="models"/>). Shared by both coherency checks.
        private static void ComputeCohesionExtents(List<DataBinding<ModelData>> models, List<Position> positions,
            List<Float2> facings, out float[] nearest, out float[] farthest)
        {
            int count = models.Count;
            nearest = new float[count];
            farthest = new float[count];
            for (int i = 0; i < count; i++) { nearest[i] = float.PositiveInfinity; farthest[i] = float.NegativeInfinity; }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_3D(positions[i], positions[j],
                        models[i].GetValue().BaseShape, facings[i],
                        models[j].GetValue().BaseShape, facings[j]);

                    nearest[i] = Math.Min(distance, nearest[i]);
                    farthest[i] = Math.Max(distance, farthest[i]);
                    nearest[j] = Math.Min(distance, nearest[j]);
                    farthest[j] = Math.Max(distance, farthest[j]);
                }
            }
        }

        public static void AssertModelInUnit(DataBinding<UnitData> unit, DataBinding<ModelData> model,
            [CallerMemberName] string methodName = null)
        {
            if (model == default)
            {
                throw new ArgumentException($"{nameof(PathTemplate)}.{methodName} called with null model.");
            }

            if (unit.GetValue().ModelBindings.Contains(model) == false)
            {
                throw new ArgumentException($"{nameof(PathTemplate)}.{methodName} called with model not in the unit.");
            }
        }

        public static string ErrorReasonToString(EErrorReasonType reason)
        {
            switch(reason)
            {
                case EErrorReasonType.OutOfMoveRange:
                    return "Unit moving too far";
                case EErrorReasonType.MovingThroughEnemyUnit:
                    return "Moves through an enemy unit";
                case EErrorReasonType.EndedTooCloseToEnemy:
                    return $"Ends within {GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES}\" of an enemy without charging it";
                case EErrorReasonType.MovingThroughImpassibleTerrain:
                    return "Moves through impassible terrain";
                case EErrorReasonType.ExceededDifficultTerrainMoveLimit:
                    return $"Moved more than {GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES}\" through difficult terrain";
                case EErrorReasonType.TooFarFromAnyUnitModel:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES} " + 
                        "inches from the closest model";
                case EErrorReasonType.TooFarFromAllUnitModels:
                    return $"Breaks cohesion: Model is further than {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES} " +
                        "inches from another model in the unit";
                case EErrorReasonType.ChargeRangeRequiresMeleeReach:
                    return $"A model moved beyond Rush range, but no model ends within {GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL}\" of an enemy";
                case EErrorReasonType.EndedOnFriendlyUnit:
                    return "Ends stacked on top of a friendly unit";
                case EErrorReasonType.EndedOffTable:
                    return "Ends with part of its base off the table";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }


    public readonly struct ReasonForInvalidMove
    {
        public readonly EErrorReasonType ErrorReasonType;
        public readonly DataBinding<ModelData> RelevantModel;


        public ReasonForInvalidMove(EErrorReasonType errorReasonType, DataBinding<ModelData> relevantModel)
        {
            ErrorReasonType = errorReasonType;
            RelevantModel = relevantModel;
        }

        public override string ToString() => MovementUtilities.ErrorReasonToString(ErrorReasonType);
    }

    public enum EErrorReasonType
    {
        OutOfMoveRange,
        MovingThroughImpassibleTerrain,
        ExceededDifficultTerrainMoveLimit,
        MovingThroughEnemyUnit,
        TooFarFromAnyUnitModel,
        TooFarFromAllUnitModels,
        ChargeRangeRequiresMeleeReach,
        EndedTooCloseToEnemy,
        EndedOnFriendlyUnit,
        /// <summary>#291 - part of the model's base would end past the table edge.</summary>
        EndedOffTable
    }

    /// <summary>
    /// A living enemy model's base footprint for movement validation: where it is, how big its base is, and
    /// a key identifying which enemy unit it belongs to (shared by all models of that unit within one
    /// footprint list). See <see cref="MovementUtilities.GetEnemyModelFootprints"/>.
    /// </summary>
    public readonly struct EnemyModelFootprint
    {
        public readonly Position Center;
        public readonly float BaseRadiusInches;
        // The enemy's true base footprint and facing (#150), used shape-aware by both the end-state gap checks
        // and the mid-path swept pass-through — so a rectangular enemy is measured by its real oriented outline.
        public readonly IBaseShape BaseShape;
        public readonly Float2 Facing;
        public readonly int UnitKey;

        /// <summary>
        /// #029: this footprint belongs to a unit that can't be moved into base contact with (Aircraft). The
        /// move validator never treats such a unit as "charged" (engaged), so a move may not end stacked on it
        /// nor within the standoff band — i.e. it can't be charged or contacted. Default false.
        /// </summary>
        public readonly bool Uncontactable;

        public EnemyModelFootprint(Position center, float baseRadiusInches, int unitKey, bool uncontactable = false,
            IBaseShape? baseShape = null, Float2? facing = null)
        {
            Center = center;
            BaseRadiusInches = baseRadiusInches;
            // No explicit shape (radius-only callers / tests) → a circle of that radius, i.e. the prior behaviour.
            BaseShape = baseShape ?? new CircleBase(baseRadiusInches);
            Facing = facing ?? new Float2(0f, 1f); // forward (+Z) — the axis-aligned default for radius-only callers
            UnitKey = unitKey;
            Uncontactable = uncontactable;
        }
    }
}
