using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// Aggregates per-piece sight-line evaluations into a single result for a given
    /// (attacker, target) pair. The terrain pieces decide their own effect; this helper
    /// just combines them with priority Blocking > Cover > Clear.
    /// </summary>
    public static class LineOfSightUtilities
    {
        /// <summary>
        /// Returns a blocker list representing every placed model on the table whose base interrupts
        /// the (attacker, defender) sight test. Which models are transparent depends on
        /// <paramref name="seeThroughFriendlyUnits"/> (#384, <see cref="GameSettings.SeeThroughFriendlyUnits"/>):
        /// false (official rules, the default game setting) excludes only the attacker's OWN unit and
        /// <paramref name="defendingUnit"/> - every other unit's models, friendly or enemy, block;
        /// true (house rule, pre-#384 behavior per #044) also excludes every model on the attacker's
        /// team. Pass the result to <see cref="EvaluateSightLine"/> / <see cref="HasLineOfSight"/>
        /// after concatenating with the normal terrain snapshot.
        /// </summary>
        public static List<ITerrain> BuildModelBlockers(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit,
            bool seeThroughFriendlyUnits)
            => BuildModelBlockers(tableState, (IUnit)attackingUnit.GetValue(), (IUnit)defendingUnit.GetValue(),
                seeThroughFriendlyUnits);

        public static List<ITerrain> BuildModelBlockers(ITableState tableState,
            IUnit attackingUnit, IUnit defendingUnit, bool seeThroughFriendlyUnits)
        {
            PlayerID attackerPlayerID = attackingUnit.PlayerID;
            ITeam? attackerTeam = tableState.Teams.Objects
                .FirstOrDefault(t => t.IsPlayerOnTeam(attackerPlayerID));

            var excluded = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            foreach (IUnit unit in tableState.Units.Objects)
            {
                // A unit that isn't in play blocks nothing: an Ambush reserve, an embarked squad, and an
                // Aircraft that flew off the edge all park their models at the origin. Asking the unit is
                // authoritative; the per-model origin check below only catches models with no unit.
                if (!unit.GetIsOnBattlefield())
                {
                    foreach (IModel m in unit.Models) excluded.Add(m);
                    continue;
                }

                if (!seeThroughFriendlyUnits) continue;
                bool isAlly = attackerTeam != null
                    ? attackerTeam.IsPlayerOnTeam(unit.PlayerID)
                    : unit.PlayerID.Equals(attackerPlayerID);
                if (!isAlly) continue;
                foreach (IModel m in unit.Models) excluded.Add(m);
            }
            // The shooter's own unit and its target are transparent under either setting: a unit
            // always ignores its own models, and "sees the unit = sees any of its models".
            foreach (IModel m in attackingUnit.Models) excluded.Add(m);
            foreach (IModel m in defendingUnit.Models) excluded.Add(m);

            var blockers = new List<ITerrain>();
            foreach (IModel model in tableState.Models.Objects)
            {
                // #158: casualties are removed from play — a dead model's base must not keep blocking
                // line of sight from wherever it happened to die.
                if (!model.GetIsAlive()) continue;
                if (model.Position.x == 0f && model.Position.z == 0f) continue;
                if (excluded.Contains(model)) continue;
                // A LoS blocker with the model's true (oriented) footprint (#150): a rectangle blocks line of
                // sight with its real shape, not its inscribed bounding circle.
                blockers.Add(new TerrainData(ETerrainType.Blocking,
                    model.BaseShape.ToZone(model.Position, model.Facing)));
            }
            return blockers;
        }

        /// <summary>
        /// #384 AI-awareness helper: blockers for the models of every same-team unit OTHER than
        /// <paramref name="activeUnit"/>, for the Tactician's centroid lane tests when
        /// <see cref="GameSettings.SeeThroughFriendlyUnits"/> is OFF. Deliberately friendlies-only:
        /// the lane approximation keeps ignoring third-party ENEMY bases (#363) - and a full
        /// per-target <see cref="BuildModelBlockers"/> list would let a target's own models shadow
        /// the centroid it is being sighted at.
        /// </summary>
        public static List<ITerrain> BuildFriendlySightBlockers(ITableState tableState, IUnit activeUnit)
        {
            PlayerID activePlayerID = activeUnit.PlayerID;
            ITeam? activeTeam = tableState.Teams.Objects
                .FirstOrDefault(t => t.IsPlayerOnTeam(activePlayerID));

            var blockers = new List<ITerrain>();
            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (unit.ID.Equals(activeUnit.ID)) continue;
                if (!unit.GetIsOnBattlefield()) continue;
                bool isAlly = activeTeam != null
                    ? activeTeam.IsPlayerOnTeam(unit.PlayerID)
                    : unit.PlayerID.Equals(activePlayerID);
                if (!isAlly) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (model.Position.x == 0f && model.Position.z == 0f) continue;
                    blockers.Add(new TerrainData(ETerrainType.Blocking,
                        model.BaseShape.ToZone(model.Position, model.Facing)));
                }
            }
            return blockers;
        }

        public static ESightLineEffect EvaluateSightLine(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
        {
            if (terrain == null)
            {
                return ESightLineEffect.Clear;
            }

            ESightLineEffect worst = ESightLineEffect.Clear;

            foreach (ITerrain piece in terrain)
            {
                ESightLineEffect effect = piece.EvaluateSightLine(attacker, target);
                if (effect > worst)
                {
                    worst = effect;
                    if (worst == ESightLineEffect.Blocking)
                    {
                        //Can't get worse than this — early-out.
                        return worst;
                    }
                }
            }

            return worst;
        }

        /// <summary>
        /// #201 variant: same worst-effect fold, but a piece evaluating to Cover is demoted to Clear
        /// when the proximity exceptions void it (shooter hugging the piece / shared cover at knife
        /// range - see <see cref="CoverProximityRules"/>). Pass the endpoint models' base facts in
        /// <paramref name="coverContext"/>; <paramref name="applyProximityExceptions"/> false makes
        /// this identical to the 3-arg overload (the lobby toggle OFF path). The 3-arg overload's
        /// semantics are deliberately untouched - PolarSightMap mirrors THAT function.
        /// </summary>
        public static ESightLineEffect EvaluateSightLine(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain, in CoverContext coverContext, bool applyProximityExceptions)
        {
            if (terrain == null)
            {
                return ESightLineEffect.Clear;
            }

            ESightLineEffect worst = ESightLineEffect.Clear;

            foreach (ITerrain piece in terrain)
            {
                ESightLineEffect effect = piece.EvaluateSightLine(attacker, target);
                if (effect == ESightLineEffect.Cover && applyProximityExceptions
                    && CoverProximityRules.VoidsCover(piece, coverContext))
                {
                    continue;
                }
                if (effect > worst)
                {
                    worst = effect;
                    if (worst == ESightLineEffect.Blocking)
                    {
                        //Can't get worse than this — early-out.
                        return worst;
                    }
                }
            }

            return worst;
        }

        /// <summary>
        /// Convenience wrapper for the binary "can attacker see target" check.
        /// Cover does not block sight; only Blocking does.
        /// </summary>
        public static bool HasLineOfSight(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
            => EvaluateSightLine(attacker, target, terrain) != ESightLineEffect.Blocking;

        /// <summary>
        /// Returns the position along the segment from <paramref name="attacker"/> to
        /// <paramref name="target"/> where the closest Blocking piece first interrupts
        /// the sight line, plus the piece that interrupted it, or null if nothing
        /// Blocking lies on the segment. Cover pieces are ignored — only Blocking is
        /// considered. Intended for UI overlays that want to draw "blocked here"
        /// indicators; do not use it for the "can the attacker see the target" gate
        /// (use <see cref="HasLineOfSight"/> for that).
        /// </summary>
        public static (Position hit, ITerrain piece)? GetFirstBlockingHit(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
        {
            if (terrain == null) return null;

            Float2 a = new Float2(attacker.x, attacker.z);
            Float2 b = new Float2(target.x, target.z);
            float bestDistSq = float.MaxValue;
            Float2 bestHit = default;
            ITerrain? bestPiece = null;

            foreach (ITerrain piece in terrain)
            {
                if (piece.EvaluateSightLine(attacker, target) != ESightLineEffect.Blocking) continue;
                Float2? entry = piece.Shape.GetFirstSegmentEntry(a, b);
                if (!entry.HasValue) continue;

                float dx = entry.Value.X - a.X;
                float dy = entry.Value.Y - a.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestHit = entry.Value;
                    bestPiece = piece;
                }
            }

            if (bestPiece == null) return null;
            return (new Position(bestHit.X, bestHit.Y), bestPiece);
        }
    }
}
