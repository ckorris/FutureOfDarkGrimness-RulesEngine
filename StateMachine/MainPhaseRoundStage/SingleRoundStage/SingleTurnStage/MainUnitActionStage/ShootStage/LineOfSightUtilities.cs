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
        /// Returns a blocker list representing every placed model on the table that
        /// does not belong to a unit on the attacker's team and does not belong to
        /// <paramref name="defendingUnit"/>. Per the game rules a unit's sight line
        /// passes through models of its own player and allied players, so every
        /// same-team model is excluded. Pass the result to <see cref="EvaluateSightLine"/>
        /// / <see cref="HasLineOfSight"/> after concatenating with the normal terrain
        /// snapshot.
        /// </summary>
        public static List<ITerrain> BuildModelBlockers(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit)
        {
            PlayerID attackerPlayerID = attackingUnit.GetValue().PlayerID;
            ITeam? attackerTeam = tableState.Teams.Objects
                .FirstOrDefault(t => t.IsPlayerOnTeam(attackerPlayerID));

            var excluded = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            foreach (IUnit unit in tableState.Units.Objects)
            {
                bool isAlly = attackerTeam != null
                    ? attackerTeam.IsPlayerOnTeam(unit.PlayerID)
                    : unit.PlayerID.Equals(attackerPlayerID);
                if (!isAlly) continue;
                foreach (IModel m in unit.Models) excluded.Add(m);
            }
            foreach (DataBinding<ModelData> b in defendingUnit.ModelBindings())
                excluded.Add(b.GetValue());

            var blockers = new List<ITerrain>();
            foreach (IModel model in tableState.Models.Objects)
            {
                if (model.Position.x == 0f && model.Position.z == 0f) continue;
                if (excluded.Contains(model)) continue;
                blockers.Add(new TerrainData(ETerrainType.Blocking,
                    new CircularZone(model.Position.x, model.Position.z, model.BaseRadiusInches)));
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
        /// Convenience wrapper for the binary "can attacker see target" check.
        /// Cover does not block sight; only Blocking does.
        /// </summary>
        public static bool HasLineOfSight(Position attacker, Position target,
            IEnumerable<ITerrain>? terrain)
            => EvaluateSightLine(attacker, target, terrain) != ESightLineEffect.Blocking;
    }
}
