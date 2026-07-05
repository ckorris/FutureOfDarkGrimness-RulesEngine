using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
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
        /// <summary>The batched dangerous-terrain test result: the single roll (one d6 per testing model),
        /// how many models tested, and total wounds dealt (fractional under the probabilistic roller).
        /// Returned so the (async) caller can present one dice beat for the whole batch.</summary>
        public readonly struct DangerousTerrainResult
        {
            public readonly IDiceResults? Roll;
            public readonly int ModelCount;
            public readonly float Wounds;
            public DangerousTerrainResult(IDiceResults? roll, int modelCount, float wounds)
            {
                Roll = roll; ModelCount = modelCount; Wounds = wounds;
            }
            public bool AnyTested => ModelCount > 0 && Roll != null;
            public static DangerousTerrainResult None => new DangerousTerrainResult(null, 0, 0f);
        }

        /// <summary>
        /// Every model whose path crosses dangerous terrain takes a test; they all roll together in ONE
        /// batch (a wound on a 1). Batching is what lets the probabilistic roller work properly here -- a
        /// single N-die roll yields the expected number of 1s, which N separate decisive rolls could not.
        /// Returns the batch so the (async) caller can present one dice beat; out-of-band callers that
        /// don't present simply ignore the return.
        /// </summary>
        public static DangerousTerrainResult ApplyDangerousTerrainEffects(IGameContext gameContext,
            IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> relevantTerrain, string unitName,
            bool ignoresDangerousTerrain = false, bool countsAsInDangerousTerrain = false)
        {
            // Flying (AllTerrain scope) ignores Dangerous-terrain effects entirely — no roll, no wounds —
            // including a "counts as in Dangerous Terrain" grant (ignoring the real effect ignores the
            // counted-as one).
            if (ignoresDangerousTerrain) return DangerousTerrainResult.None;

            List<ITerrain> dangerous = relevantTerrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList();

            // "Counts as being in Dangerous Terrain" (#153): every moving model tests, regardless of what
            // its path actually crosses — so the no-dangerous-on-table early-out doesn't apply.
            if (dangerous.Count == 0 && !countsAsInDangerousTerrain) return DangerousTerrainResult.None;

            List<ModelMoveEntry> testers = new List<ModelMoveEntry>();
            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                if (!countsAsInDangerousTerrain
                    && !MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous)) continue;
                testers.Add(move);
            }
            if (testers.Count == 0) return DangerousTerrainResult.None;

            // One batched roll: N d6 at once. Realistic -> N concrete dice; probabilistic -> the N-die
            // distribution (expected 1s).
            IDiceResults roll = gameContext.DiceRoller.Roll(6, testers.Count);
            float ones = roll.At(1); // 1s are wounds (fractional under the probabilistic roller)

            float woundsDealt;
            if (gameContext.Settings.RandomnessType == ERandomnessType.Probabilistic)
            {
                // Spread the expected wounds evenly -- each model carried its own 1/6 chance of a 1.
                float perModel = ones / testers.Count;
                foreach (ModelMoveEntry move in testers)
                    move.Model.GetValue().DealWounds(perModel);
                woundsDealt = ones;
            }
            else
            {
                // Realistic: a whole number of 1s came up; deal one wound apiece to that many models.
                int wounds = (int)MathF.Round(ones);
                for (int i = 0; i < wounds && i < testers.Count; i++)
                    testers[i].Model.GetValue().DealWounds(1);
                woundsDealt = wounds;
            }

            gameContext.Log($"{unitName}: {testers.Count} model(s) tested dangerous terrain - {woundsDealt:0.##} wound(s) dealt.");
            return new DangerousTerrainResult(roll, testers.Count, woundsDealt);
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
                        // Per-waypoint facing (#150), when the resolver supplied it (default: direction of travel).
                        if (modelEntry.Facings != null && i < modelEntry.Facings.Count)
                            modelEntry.Model.GetValue().SetFacing(modelEntry.Facings[i]);
                    }
                }
            }
        }

        /// <summary>
        /// The full move sequence for an out-of-band move: apply dangerous-terrain effects, then
        /// commit positions. Mirrors the normal flow's
        /// <c>ApplyNonMovementTerrainEffectsStage</c> → <c>ExecuteMoveStage</c> order. Returns the
        /// dangerous-terrain rolls so the caller can present them (<see cref="PresentDangerousTerrainRolls"/>) —
        /// the normal flow presents from its stage, so the out-of-band caller must too.
        /// </summary>
        public static DangerousTerrainResult Commit(IGameContext gameContext, IReadOnlyList<ModelMoveEntry> paths,
            IEnumerable<ITerrain> relevantTerrain, string unitName, bool ignoresDangerousTerrain = false)
        {
            DangerousTerrainResult result =
                ApplyDangerousTerrainEffects(gameContext, paths, relevantTerrain, unitName, ignoresDangerousTerrain);
            CommitPositions(paths);
            return result;
        }

        /// <summary>
        /// Presents the whole dangerous-terrain batch as a single <see cref="DiceRolledBeat"/> (one d6 per
        /// testing model: 2+ safe/green, a 1 a wound). Shared by the normal-move stage and the out-of-band
        /// (triggered) move so a Vanguard / forced move shows the same roll the player sees on a normal move.
        /// Dangerous terrain deals wounds but is NOT a morale-test source, so this presents the roll only —
        /// no morale test is run here.
        /// </summary>
        public static async Task PresentDangerousTerrainRolls(IGameContext gameContext,
            DangerousTerrainResult result)
        {
            if (!result.AnyTested) return;
            string summary = result.Wounds <= 0f
                ? "All safe"
                : $"{result.Wounds:0.##} wound{(result.Wounds == 1f ? "" : "s")}";
            await gameContext.Presenter.Present(DiceRolledBeat.From(result.Roll!, successThreshold: 2,
                gameContext.Settings.RandomnessType, "Dangerous Terrain", summary));
        }

        /// <summary>
        /// Move <paramref name="unit"/> along <paramref name="paths"/>, validated against a single
        /// distance budget of <paramref name="maxInches"/> (no charge semantics). On success the move
        /// is committed and the method returns true; on failure nothing is mutated and
        /// <paramref name="errors"/> describes why.
        /// </summary>
        public static bool TryMove(IGameContext gameContext, DataBinding<UnitData> unit,
            List<ModelMoveEntry> paths, float maxInches, out List<ReasonForInvalidMove> errors,
            out DangerousTerrainResult dangerResult)
        {
            dangerResult = DangerousTerrainResult.None;
            List<ITerrain> relevantTerrain = new List<ITerrain>(gameContext.TableState.Terrain.Objects);

            // #090: an out-of-band move (e.g. Vanguard) is enemy-aware like a normal move — it may not pass
            // through or end stacked on an enemy unless the unit may move through enemies (Strafing fly-over).
            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(unit, gameContext);
            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresAllTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);

            if (!MovementUtilities.ValidatePaths(paths, maxInches, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficultTerrain, ignoresAllTerrain, relevantTerrain, out errors))
            {
                return false;
            }

            // Flying ignores Dangerous terrain too (same AllTerrain scope as the impassible waiver above).
            dangerResult = Commit(gameContext, paths, relevantTerrain, unit.GetValue().Name, ignoresDangerousTerrain: ignoresAllTerrain);
            return true;
        }
    }
}
