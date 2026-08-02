using System.Text;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    public class ConsolidateStage : StageBase<ICombatActionContext>
    {
        public const float WIPEOUT_MAX_DISTANCE_INCHES = 3f;
        public const float DISENGAGE_MAX_DISTANCE_INCHES = 1f;

        public StageBinding OnConsolidated;

        public ConsolidateStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnConsolidated = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            bool attackerAlive = AnyAlive(context.AttackingUnit.GetValue());
            bool defenderAlive = AnyAlive(context.DefendingUnit.GetValue());

            if (!attackerAlive)
            {
                GameContext.Log("Consolidation: attacker has no living models - skipping.");
                await OnConsolidated.Activate(context);
                return;
            }

            bool wipeout = !defenderAlive;
            EConsolidationReason reason = wipeout ? EConsolidationReason.Wipeout : EConsolidationReason.Disengage;
            float maxDist = wipeout ? WIPEOUT_MAX_DISTANCE_INCHES : DISENGAGE_MAX_DISTANCE_INCHES;

            GameContext.Log($"Consolidation: {reason} (up to {maxDist}\").");

            PlayerID playerID = context.AttackingUnit.GetValue().PlayerID;

            // #090: enemy-check the consolidation move (no charge semantics). The fly-over flag lets a
            // Strafing unit consolidate through an enemy; it still may not end stacked on one.
            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                context.AttackingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                context.AttackingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresImpassibleTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                context.AttackingUnit.GetValue(), context.GameContext.RuleEvaluator);
            List<EnemyModelFootprint> enemyFootprints =
                MovementUtilities.GetEnemyModelFootprints(context.AttackingUnit, context.GameContext);
            // #205: a consolidation move may not end stacked on a friendly unit either.
            List<EnemyModelFootprint> friendlyFootprints =
                MovementUtilities.GetFriendlyModelFootprints(context.AttackingUnit, context.GameContext);

            var request = new ConsolidationMoveRequest(playerID,
                reason == EConsolidationReason.Wipeout
                    ? "Consolidating After a Wipeout" : "Consolidating After Disengaging",
                context.AttackingUnit, maxDist, reason, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain);

            List<ModelMoveEntry> movements = await context.PlayerRequester()
                .RequestDecision<ConsolidationMoveRequest, List<ModelMoveEntry>>(request);

            // Validate against the cap, terrain, and enemy footprints. Coherency is LENIENT here (#159): a unit
            // left out of coherency by a mid-unit casualty can't always re-form within the 1-3" consolidation
            // cap, so the move is only rejected for coherency if it makes coherency WORSE than it already was.
            // (The resolvers still re-form toward the group within the cap — this just keeps a badly-holed unit
            // from being trapped with no legal consolidation.)
            IEnumerable<ITerrain>? terrain = context.GameContext.TableState.Terrain.Objects;
            if (!MovementUtilities.ValidateConsolidationPaths(movements, maxDist, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficultTerrain, ignoresImpassibleTerrain, terrain, out List<ReasonForInvalidMove> errors,
                    friendlyFootprints))
            {
                StringBuilder sb = new StringBuilder(errors[0].ToString());
                for (int i = 1; i < errors.Count; i++) sb.Append(", ").Append(errors[i].ToString());
                throw new RequestResponseInvalidException(
                    $"Response to {nameof(ConsolidateStage)} was invalid: {sb}");
            }

            ApplyMovements(movements);
            await OnConsolidated.Activate(context);
        }

        private static bool AnyAlive(UnitData unit)
        {
            foreach (var binding in unit.ModelBindings)
                if (binding.GetValue().GetIsAlive()) return true;
            return false;
        }

        private static void ApplyMovements(List<ModelMoveEntry> movements)
        {
            foreach (ModelMoveEntry entry in movements)
            {
                if (entry.Positions.Count == 0) continue;
                for (int i = 0; i < entry.Positions.Count; i++)
                {
                    entry.Model.GetValue().SetPosition(entry.Positions[i]);
                    // #283: apply per-waypoint facings when the entry carries them (the GUI's group-mode
                    // rotation), same contract as MovementExecutor. Entries without facings keep the
                    // model's resting facing (CLI/AI resolvers).
                    if (entry.Facings != null && i < entry.Facings.Count)
                        entry.Model.GetValue().SetFacing(entry.Facings[i]);
                }
            }
        }
    }
}
