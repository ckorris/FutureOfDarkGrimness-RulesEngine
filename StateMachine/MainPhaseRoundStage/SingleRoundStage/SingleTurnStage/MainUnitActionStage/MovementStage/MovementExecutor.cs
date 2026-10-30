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
    /// that rolls the dangerous-terrain test for a move (<see cref="RollDangerousTerrain"/>; its
    /// "activates in it" sibling, <see cref="RollActivationDangerousTerrain"/>, lives beside it, #446) and
    /// one place that commits positions (<see cref="CommitPositions"/>). The two stay separate
    /// methods because the normal flow runs them as distinct stages — merging them would
    /// double-apply terrain effects.
    /// </para>
    /// </summary>
    public static class MovementExecutor
    {
        /// <summary>The batched dangerous-terrain test result: the single roll (#420: one d6 per wound it
        /// would take to fully destroy the unit), how many dice that was, total wounds rolled (fractional
        /// under the probabilistic roller), the per-model wounds still PENDING, and who is testing. Returned
        /// so the (async) caller can present one dice beat for the whole batch and then land the wounds -
        /// see <see cref="ResolveDangerousTerrain"/>.</summary>
        public readonly struct DangerousTerrainResult
        {
            public readonly IDiceResults? Roll;
            /// <summary>The dice rolled: the unit's wounds to destroy at the moment of the test.</summary>
            public readonly int DiceCount;
            public readonly float Wounds;
            /// <summary>The wounds this test owes, NOT yet applied. Landed by <see cref="ResolveDangerousTerrain"/>.</summary>
            public readonly IReadOnlyList<PendingModelWound> PendingWounds;
            /// <summary>
            /// The unit that tested. Carried as the unit itself (not just its ID) because
            /// <see cref="ResolveDangerousTerrain"/> needs it for the destruction seam when the test wipes
            /// the unit out, not only for the casualty beats. Null only on <see cref="None"/>.
            /// </summary>
            public readonly IUnit? Unit;
            /// <summary>
            /// #446: why the test was owed, when the reason is not the default "a move crossed it" - e.g.
            /// <see cref="ACTIVATED_IN_TRIGGER"/>. Named on the dice row and the log line so a test that fires
            /// before the unit has done anything reads as the rule, not as a phantom roll. Null for a move or
            /// a forced test, whose rows read as before.
            /// </summary>
            public readonly string? Trigger;

            public DangerousTerrainResult(IDiceResults? roll, int diceCount, float wounds,
                IReadOnlyList<PendingModelWound> pendingWounds, IUnit? unit, string? trigger = null)
            {
                Roll = roll; DiceCount = diceCount; Wounds = wounds;
                PendingWounds = pendingWounds; Unit = unit; Trigger = trigger;
            }
            public bool AnyTested => DiceCount > 0 && Roll != null && Unit != null;
            public static DangerousTerrainResult None =>
                new DangerousTerrainResult(null, 0, 0f, Array.Empty<PendingModelWound>(), null);
        }

        /// <summary>#446: the trigger text for a unit that ACTIVATED standing in Dangerous terrain - shown on the
        /// dice row ("Unit: activated in Dangerous terrain - 5 dice (wounds to destroy)") and the log line.
        /// ASCII only (game text).</summary>
        public const string ACTIVATED_IN_TRIGGER = "activated in Dangerous terrain";

        /// <summary>
        /// 3.6 (#420): "Units with models moving across Dangerous terrain ... must roll as many dice as the
        /// number of wounds it would take to fully destroy them, and for each roll of 1 the unit takes one
        /// wound." So ANY model whose path crosses dangerous terrain (or any moving model, under a "counts
        /// as Dangerous" grant) puts the whole UNIT to the test, and the unit rolls ONE batch of
        /// <see cref="IUnitExtensions.GetWoundsToDestroy"/> dice - not a die per crossing model. Batching is
        /// what lets the probabilistic roller work properly here -- a single N-die roll yields the expected
        /// number of 1s, which N separate decisive rolls could not.
        /// <para>
        /// Rolls only: the wounds come back PENDING and are landed later by
        /// <see cref="ResolveDangerousTerrain"/>, after the move has been animated, so a casualty falls
        /// at its destination instead of vanishing from the start line. Rolling here keeps the dice draw
        /// in its original place in the seeded stream. Out-of-band callers that only want the state
        /// change can land the batch with <see cref="CasualtyPresentation.ApplyOnly"/>.
        /// </para>
        /// <para>
        /// #446: one test per activation on either trigger. A unit that has already tested this activation -
        /// because it activated standing in Dangerous terrain, or on an earlier move - rolls nothing here
        /// (<see cref="Rules.Dispatch.DangerousTerrainTestedThisActivation"/>, ruled in WorkItems/446 D2), and a
        /// test rolled here marks it. A "counts as Dangerous" move is the same test and obeys the same marker.
        /// </para>
        /// </summary>
        public static DangerousTerrainResult RollDangerousTerrain(IGameContext gameContext,
            IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> relevantTerrain, IUnit unit,
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

            // One crossing model is enough to put the unit to the test; the rest of the unit rolls with it.
            bool anyModelCrosses = false;
            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                if (!countsAsInDangerousTerrain
                    && !MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous)) continue;
                anyModelCrosses = true;
                break;
            }
            if (!anyModelCrosses) return DangerousTerrainResult.None;

            if (Rules.Dispatch.DangerousTerrainTestedThisActivation.Has(unit))
            {
                gameContext.Log($"{unit.Name}: already took its dangerous terrain test this activation - " +
                    "no second test for this move.");
                return DangerousTerrainResult.None;
            }

            DangerousTerrainResult result = RollBatch(gameContext, unit);
            if (result.AnyTested) Rules.Dispatch.DangerousTerrainTestedThisActivation.Mark(unit);
            return result;
        }

        /// <summary>
        /// #446 - the other half of the rule: "Units with models moving across Dangerous terrain, OR THAT
        /// ACTIVATE IN IT, must roll as many dice as the number of wounds it would take to fully destroy
        /// them". Called once at the very start of the unit's activation (<c>ActivatedInDangerousTerrainStage</c>):
        /// if any LIVING model's base overlaps a Dangerous piece where it stands
        /// (<see cref="MovementUtilities.IsModelInDangerousTerrain"/>, the move's own footprint test), the
        /// unit rolls the same #420 pool once and is marked as tested for this activation, so moving out
        /// across the piece afterwards does not test again (WorkItems/446 D2).
        /// <para>
        /// No test for: a unit with nothing alive; an embarked unit (its models are inside the transport, not
        /// on the table - the transport tests for itself); an Aircraft that flew off the table; a unit that
        /// ignores Dangerous terrain (Flying / Aircraft, the AllTerrain scope of
        /// <see cref="Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain"/>; Strider is Difficult-only and
        /// does test); a unit that has already tested this activation. A "counts as being in Dangerous
        /// Terrain" grant is NOT a trigger here: it is hung on the unit's next MOVE and is read by
        /// <see cref="RollDangerousTerrain"/> (WorkItems/446 D4).
        /// </para>
        /// Rolls only; the caller lands the wounds with <see cref="ResolveDangerousTerrain"/>.
        /// </summary>
        public static DangerousTerrainResult RollActivationDangerousTerrain(IGameContext gameContext, IUnit unit,
            IEnumerable<ITerrain> terrain)
        {
            if (!unit.GetIsAlive()) return DangerousTerrainResult.None;
            if (Rules.Dispatch.TransportUtilities.IsEmbarked(unit)) return DangerousTerrainResult.None;
            if (unit.Tokens.HasToken(Rules.Foundation.TokenType.OffTableFromForcedMove))
                return DangerousTerrainResult.None;

            List<ITerrain> dangerous = terrain.Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)).ToList();
            if (dangerous.Count == 0) return DangerousTerrainResult.None;

            bool anyModelInside = unit.Models.Any(model => model.GetIsAlive()
                && MovementUtilities.IsModelInDangerousTerrain(model, dangerous));
            if (!anyModelInside) return DangerousTerrainResult.None;

            // Flying ignores the effects of Dangerous terrain - standing in it as much as crossing it.
            if (Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(unit, gameContext.RuleEvaluator))
                return DangerousTerrainResult.None;

            if (Rules.Dispatch.DangerousTerrainTestedThisActivation.Has(unit)) return DangerousTerrainResult.None;

            DangerousTerrainResult result = RollBatch(gameContext, unit, ACTIVATED_IN_TRIGGER);
            if (result.AnyTested) Rules.Dispatch.DangerousTerrainTestedThisActivation.Mark(unit);
            return result;
        }

        /// <summary>
        /// A dangerous-terrain test forced on <paramref name="unit"/> without any move: the unit rolls one
        /// die per wound it would take to destroy it (#420), wherever it stands. The immediate arm of #197 P8's Dangerous Terrain Debuff
        /// ("pick one enemy unit within 18\", which must immediately take a Dangerous Terrain test") —
        /// distinct from the deferred "counts as being in Dangerous Terrain once" arm, which rides the
        /// unit's next move through <see cref="RollDangerousTerrain"/> instead.
        /// <para>
        /// Same waiver as a real crossing: a Flying target (the AllTerrain ignore scope)
        /// takes no test at all. Owner-ruled 2026-07-28 — one rule for all three dangerous-terrain paths
        /// beats a debuff that ignores what every other terrain effect honours. Strider is DifficultOnly
        /// and so does NOT waive it, exactly as when it walks through the real thing.
        /// </para>
        /// Rolls only; the caller lands the wounds with <see cref="ResolveDangerousTerrain"/>.
        /// </summary>
        public static DangerousTerrainResult RollForcedDangerousTerrain(IGameContext gameContext, IUnit unit,
            bool ignoresDangerousTerrain = false)
        {
            if (ignoresDangerousTerrain) return DangerousTerrainResult.None;

            return RollBatch(gameContext, unit);
        }

        /// <summary>
        /// The batched roll shared by the move-driven and forced tests: ONE roll of N d6 for the N wounds it
        /// would take to destroy the unit, each 1 a wound to the unit (#420). The rolling, the
        /// probabilistic spread and the wound assignment are <see cref="DangerousTerrainBatch"/>'s, shared
        /// with the transport spillout test; this just carries the result in the movement flow's shape.
        /// </summary>
        private static DangerousTerrainResult RollBatch(IGameContext gameContext, IUnit unit, string? trigger = null)
        {
            Rules.Dispatch.DangerousTerrainBatch.Outcome test = Rules.Dispatch.DangerousTerrainBatch.Roll(unit, gameContext.DiceRoller,
                gameContext.Settings.RandomnessType);
            if (test.DiceCount <= 0) return DangerousTerrainResult.None;

            List<PendingModelWound> pending = test.PendingWounds
                .Select(w => new PendingModelWound(w.Model, w.Wounds)).ToList();

            string reason = trigger == null ? "" : $" ({trigger})";
            gameContext.Log($"{unit.Name}: dangerous terrain test{reason} - {test.DiceCount} " +
                $"{(test.DiceCount == 1 ? "die" : "dice")} (wounds to destroy), {test.Wounds:0.##} wound(s) dealt.");
            return new DangerousTerrainResult(test.Roll, test.DiceCount, test.Wounds, pending, unit, trigger);
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
        /// The full move sequence for an out-of-band move: roll the dangerous-terrain test, then commit
        /// positions. Mirrors the normal flow's <c>ApplyNonMovementTerrainEffectsStage</c> →
        /// <c>ExecuteMoveStage</c> order. The wounds come back PENDING; the caller lands and animates
        /// them with <see cref="ResolveDangerousTerrain"/> once the models are where they end up — the
        /// normal flow resolves from its stage, so the out-of-band caller must too.
        /// </summary>
        public static DangerousTerrainResult Commit(IGameContext gameContext, IReadOnlyList<ModelMoveEntry> paths,
            IEnumerable<ITerrain> relevantTerrain, IUnit unit, bool ignoresDangerousTerrain = false)
        {
            DangerousTerrainResult result =
                RollDangerousTerrain(gameContext, paths, relevantTerrain, unit, ignoresDangerousTerrain);
            CommitPositions(paths);
            return result;
        }

        /// <summary>
        /// Lands a rolled dangerous-terrain batch: the whole roll as a single <see cref="DiceRolledBeat"/>
        /// (one d6 per wound the unit would need to be destroyed: 2+ safe/green, a 1 a wound), then the wounds themselves, each
        /// model's death or flinch animating as its wound lands (<see cref="CasualtyPresentation"/>).
        /// Shared by the normal-move stage and the out-of-band (triggered) move so a Harassing / forced
        /// move shows the same sequence the player sees on a normal move.
        /// <para>
        /// Called AFTER the move has been animated, so a casualty falls at the destination it walked to.
        /// Dangerous terrain deals wounds but is NOT a morale-test source, so no morale test is run here —
        /// but a test that wipes the unit out IS a destruction, and goes through the destruction seam.
        /// </para>
        /// </summary>
        public static async Task ResolveDangerousTerrain(IGameContext gameContext,
            DangerousTerrainResult result)
        {
            if (!result.AnyTested) return;
            IUnit unit = result.Unit!;

            string summary = result.Wounds <= 0f
                ? "All safe"
                : $"{result.Wounds:0.##} wound{(result.Wounds == 1f ? "" : "s")}";
            await gameContext.Presenter.Present(DiceRolledBeat.From(result.Roll!, successThreshold: 2,
                gameContext.Settings.RandomnessType, "Dangerous Terrain", summary,
                context: DescribeDice(unit, result.DiceCount, result.Trigger)));

            bool wasAlive = unit.GetIsAlive();

            await CasualtyPresentation.ApplyAndPresent(gameContext, result.PendingWounds, unit);

            // Dangerous terrain can finish a unit off, and that death is a destruction like any other:
            // the dead unit's OwnerDestroyed marks on OTHER units have to clear, and if it was a Transport
            // its cargo has to spill rather than stay embarked in a wreck that no longer exists (the #169
            // ghost state, reachable through terrain). Killer-less, so the Shooting_OnUnitDestroyed hook
            // correctly stays silent - there is no attacker to credit.
            if (wasAlive && !unit.GetIsAlive())
            {
                await UnitDestructionNotifier.NotifyUnitDestroyed(gameContext, unit, killer: null);
            }
        }

        /// <summary>The "who and how many" line under a dangerous-terrain dice row (#420): the pool is the
        /// unit's wounds to destroy, and a player who sees nine dice for a three-model squad should be able
        /// to read why. Shared with the spillout presentation. #446: a non-move trigger is named before the
        /// count ("Unit: activated in Dangerous terrain - 5 dice (wounds to destroy)").</summary>
        public static string DescribeDice(IUnit unit, int diceCount, string? trigger = null) =>
            $"{unit.Name}: {(trigger == null ? "" : trigger + " - ")}{diceCount} " +
            $"{(diceCount == 1 ? "die" : "dice")} (wounds to destroy)";

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

            // #090: an out-of-band move (e.g. Harassing) is enemy-aware like a normal move — it may not pass
            // through or end stacked on an enemy unless the unit may move through enemies (Strafing fly-over).
            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(unit, gameContext);
            // #205: a triggered move (Harassing / Hit & Run / forced move) may not end stacked on a friendly.
            List<EnemyModelFootprint> friendlyFootprints = MovementUtilities.GetFriendlyModelFootprints(unit, gameContext);
            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresAllTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);

            if (!MovementUtilities.ValidatePaths(paths, maxInches, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficultTerrain, ignoresAllTerrain, relevantTerrain, out errors, friendlyFootprints))
            {
                return false;
            }

            // Flying ignores Dangerous terrain too (same AllTerrain scope as the impassible waiver above).
            dangerResult = Commit(gameContext, paths, relevantTerrain, unit.GetValue(), ignoresDangerousTerrain: ignoresAllTerrain);
            return true;
        }
    }
}
