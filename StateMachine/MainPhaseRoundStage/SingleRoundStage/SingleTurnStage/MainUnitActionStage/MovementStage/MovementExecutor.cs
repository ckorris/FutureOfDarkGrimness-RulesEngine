using FDG.Data;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// The movement subsystem as a callable primitive — the engine refactor (#042 Phase 7h)
    /// that lets a unit be moved from outside the normal turn/stage flow (e.g. a rule's
    /// <see cref="Rules.Definitions.RuleOperation.InvokeTriggeredMove"/>, mid-move attacks,
    /// post-deploy repositions).
    /// <para>
    /// The normal movement stages delegate their bodies here so there is exactly one place
    /// that applies dangerous-terrain effects (<see cref="ApplyDangerousTerrainEffects"/>) and
    /// one place that commits positions (<see cref="CommitPositions"/>). The two stay separate
    /// methods because the normal flow runs them as distinct stages — merging them would
    /// double-apply terrain effects.
    /// </para>
    /// </summary>
    public static class MovementExecutor
    {
        /// <summary>One model's dangerous-terrain check: the face it rolled and whether it took a wound
        /// (a 1). Returned so a caller on the presentation path can show each die; the logic itself
        /// (rolling + dealing the wound) happens here regardless.</summary>
        public readonly struct DangerousTerrainRoll
        {
            public readonly int Roll;
            public readonly bool Wounded;
            public DangerousTerrainRoll(int roll, bool wounded) { Roll = roll; Wounded = wounded; }
        }

        /// <summary>
        /// Roll for each model whose path crosses dangerous terrain, dealing a wound on a 1. Returns the
        /// per-model rolls so the (async) caller can present a dice beat for each; out-of-band callers
        /// that don't present simply ignore the return.
        /// </summary>
        public static IReadOnlyList<DangerousTerrainRoll> ApplyDangerousTerrainEffects(IGameContext gameContext,
            IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> relevantTerrain, string unitName)
        {
            List<DangerousTerrainRoll> results = new List<DangerousTerrainRoll>();

            List<ITerrain> dangerous = relevantTerrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList();

            if (dangerous.Count == 0) return results;

            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;

                if (!MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous)) continue;

                // Decisive per-model die — one concrete face even under the probabilistic roller (#090).
                IDiceResults roll = gameContext.DiceRoller.RollDecisive(6);

                // Die faces start at SideMin (1), not 0 — find which face came up.
                int rollValue = roll.SideMin;
                for (int v = roll.SideMin; v <= roll.SideMax; v++)
                    if (roll.At(v) > 0f) { rollValue = v; break; }

                bool wounded = roll.At(1) > 0;
                if (wounded)
                {
                    move.Model.GetValue().DealWounds(1);
                    gameContext.Log($"{unitName}: model crossed dangerous terrain, rolled {rollValue} - 1 wound dealt.");
                }
                else
                {
                    gameContext.Log($"{unitName}: model crossed dangerous terrain, rolled {rollValue} - safe.");
                }

                results.Add(new DangerousTerrainRoll(rollValue, wounded));
            }

            return results;
        }

        /// <summary>
        /// Commit the final model positions for each move entry. Body lifted verbatim from
        /// <c>ExecuteMoveStage</c>.
        /// </summary>
        public static void CommitPositions(IReadOnlyList<ModelMoveEntry> paths)
        {
            foreach (ModelMoveEntry modelEntry in paths)
            {
                if (modelEntry.Positions.Count > 0)
                {
                    //Setting each position may be redundant for awhile, but we might add some kind of animation
                    //where the position updates queue up. So, we'll do this anyway.
                    for (int i = 0; i < modelEntry.Positions.Count; i++)
                    {
                        modelEntry.Model.GetValue().SetPosition(modelEntry.Positions[i]);
                    }
                }
            }
        }

        /// <summary>
        /// The full move sequence for an out-of-band move: apply dangerous-terrain effects, then
        /// commit positions. Mirrors the normal flow's
        /// <c>ApplyNonMovementTerrainEffectsStage</c> → <c>ExecuteMoveStage</c> order.
        /// </summary>
        public static void Commit(IGameContext gameContext, IReadOnlyList<ModelMoveEntry> paths,
            IEnumerable<ITerrain> relevantTerrain, string unitName)
        {
            ApplyDangerousTerrainEffects(gameContext, paths, relevantTerrain, unitName);
            CommitPositions(paths);
        }

        /// <summary>
        /// Move <paramref name="unit"/> along <paramref name="paths"/>, validated against a single
        /// distance budget of <paramref name="maxInches"/> (no charge semantics). On success the move
        /// is committed and the method returns true; on failure nothing is mutated and
        /// <paramref name="errors"/> describes why.
        /// </summary>
        public static bool TryMove(IGameContext gameContext, DataBinding<UnitData> unit,
            List<ModelMoveEntry> paths, float maxInches, out List<ReasonForInvalidMove> errors)
        {
            List<ITerrain> relevantTerrain = new List<ITerrain>(gameContext.TableState.Terrain.Objects);

            // #090: an out-of-band move (e.g. Vanguard) is enemy-aware like a normal move — it may not pass
            // through or end stacked on an enemy unless the unit may move through enemies (Strafing fly-over).
            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(unit, gameContext);
            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);

            if (!MovementUtilities.ValidatePaths(paths, maxInches, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficultTerrain, relevantTerrain, out errors))
            {
                return false;
            }

            Commit(gameContext, paths, relevantTerrain, unit.GetValue().Name);
            return true;
        }
    }
}
