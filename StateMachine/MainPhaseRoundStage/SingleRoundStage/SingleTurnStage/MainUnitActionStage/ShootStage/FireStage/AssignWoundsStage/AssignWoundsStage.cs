using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Threading.Tasks;

namespace FDG.Stages
{

    public class AssignWoundsStage<TMetadata> : CombatStage<AssignWoundsResults, AssignWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public AssignWoundsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<AssignWoundsResults, Task> onFinished)
        {
            metaData.QueryForResult(out RollToSaveResults rollToSaveResults);

            float totalWoundsDealt = 0;
            foreach (FailedSaveInfo failedSaves in rollToSaveResults.FailedSaveList)
            {
                totalWoundsDealt += failedSaves.SaveCount;
            }

            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();

            // #197: the same measurement RollToHitStage takes, so a save-side rule can be range-gated the way
            // a hit-side one is. Only meaningful for shooting; a melee save is always rolled in base contact,
            // which is why the Boost rules read the charge's launch distance instead (see
            // IHasAttackOriginDistance).
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(
                attacker, defender, out _, out _, includeVertical: true);

            // #042 save-roll-complete rules (Bane reroll, Regeneration ignore, plus the suppressors'
            // ignore-Regeneration facet) all fire here. Evaluate BOTH participants once, so the
            // evaluator's suppression first-pass can cancel Regeneration before its op is folded. The
            // resulting queue feeds the reroll (below, before Deadly) and the wound-ignore (after Deadly).
            IReadOnlyList<RuleOperation> saveCompleteOperations = GameContext.RuleEvaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker, defender, CombineSaveRolls(rollToSaveResults),
                    metaData.IsMelee, metaData.IsSpell, distance, metaData.ChargeOriginDistanceInches,
                    // #376 (Grounded Protection): terrain-proximity save-side rules read the live layout.
                    GameContext.TableState.Terrain.Objects.ToList()),
                RuleParticipant.Actor(attacker, metaData.WeaponType),
                // #183: the defender's living models surface a joined hero's relocated wound-ignore rules
                // (Regeneration/Resistance/Protected), gated by AllModelsHaveThisRule - so a sole-surviving
                // hero regenerates, and the trace shows the gate deciding while grunts live.
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));

            // #042 save-reroll rules (Bane): the defender re-rolls its highest unmodified saves, turning
            // saved dice into possible failures. The threshold is normally the unmodified max (6); a Boost
            // variant widens it to 5-6 (#197). Re-roll each successful group's qualifying count and add the
            // new failures to the wound total — done BEFORE Deadly multiplies, since it finalizes the saves.
            RerollSink rerollSink = new RerollSink();
            rerollSink.ApplyFrom(saveCompleteOperations);
            if (rerollSink.RerollSavesAtOrAbove is int rerollFrom)
            {
                foreach (SuccessfulSaveInfo saved in rollToSaveResults.SuccessfulSaveList)
                {
                    // Clamped to the die's own faces: an authored threshold above SideMax would otherwise
                    // silently re-roll nothing, and one below SideMin would re-roll the whole group.
                    int threshold = System.Math.Clamp(rerollFrom, saved.Rolls.SideMin, saved.Rolls.SideMax);
                    float qualifying = saved.Rolls.AtOrAbove(threshold);
                    if (qualifying <= 0f) continue;
                    int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(saved.RollNeededInfo.SaveNeeded);
                    IDiceResults rerollResult = GameContext.DiceRoller.Roll(qualifying);
                    float newWounds = rerollResult.Below(saveNeeded);
                    totalWoundsDealt += newWounds;
                    await GameContext.Presenter.Present(DiceRolledBeat.From(rerollResult, saveNeeded,
                        GameContext.Settings.RandomnessType, "Bane Re-roll", RollTags.Count(newWounds, "new wound"),
                        category: ERollBeatCategory.Defense, context: $"{defender.Name} re-saves"));
                }
            }

            // #042 wound-multiplier rules (Deadly) fire at pre-apply-wound: evaluate the attacker's
            // rules, fold MultiplyWounds ops through the sink, and scale the wound count. The stage
            // interprets no operation; it just reads the net multiplier.
            IReadOnlyList<RuleOperation> woundOperations = GameContext.RuleEvaluator.EvaluateAll(
                new PreApplyWoundContext(attacker, defender),
                RuleParticipant.Actor(attacker, metaData.WeaponType));
            WoundModifierSink woundModifier = new WoundModifierSink();
            woundModifier.ApplyFrom(woundOperations);
            if (woundModifier.NetMultiplier > 1)
            {
                // Deadly(X): each failed save becomes a clump of X wounds that lands entirely on ONE model
                // and does NOT carry over — overkill beyond that model is lost. So the multiplier is wasted
                // against single-wound models (Deadly's whole point is anti-Tough) and a clump's excess is
                // discarded against Tough models. ConfineToClumps returns the effective wound total, which
                // replaces the naive total*X (that wrongly let the multiplied wounds spill across the unit).
                totalWoundsDealt = ConfineToClumps(totalWoundsDealt, woundModifier.NetMultiplier, defender);
            }

            // #100 Shred (wound injection): "for each unmodified 1 to block, +1 wound" fires at
            // save-complete, so it rides the same saveCompleteOperations queue. Fold the wound-injection
            // sink and add the extra wounds flat — AFTER Deadly's clump confinement (Shred + Deadly on one
            // weapon isn't in the corpus, so the flat add stays out of the clump model) and BEFORE
            // Regeneration, so the defender may still ignore the Shred wounds like any others.
            WoundInjectionSink woundInjection = new WoundInjectionSink();
            woundInjection.ApplyFrom(saveCompleteOperations);
            if (woundInjection.TotalExtraWounds > 0f)
            {
                totalWoundsDealt += woundInjection.TotalExtraWounds;
                GameContext.Log($"Shred added {woundInjection.TotalExtraWounds:0.##} extra wound(s).");
            }

            // #042 wound-ignore rules (Regeneration) from the same save-complete queue: the defender
            // ignores each wound on a roll of X+. Fold the ignore sink, then roll one d6 per wound at the
            // best threshold and drop the ignored count. The stage interprets no operation.
            // ORDER: applied AFTER Deadly's multiply (the rulebook tags Deadly "resolved first"). The
            // per-wound / single-model allocation nuance of both rules stays Phase-8 (tough-aware TODO below).
            WoundIgnoreSink woundIgnore = new WoundIgnoreSink();
            woundIgnore.ApplyFrom(saveCompleteOperations);
            if (woundIgnore.HasIgnore && totalWoundsDealt > 0f)
            {
                IDiceResults regenRoll = GameContext.DiceRoller.Roll(totalWoundsDealt);
                float ignored = regenRoll.AtOrAbove(woundIgnore.Threshold);
                totalWoundsDealt -= ignored;
                await GameContext.Presenter.Present(DiceRolledBeat.From(regenRoll, woundIgnore.Threshold,
                    GameContext.Settings.RandomnessType, "Regeneration", $"{ignored:0.##} ignored",
                    category: ERollBeatCategory.Defense, context: defender.Name));

                // #197 P12: the wound-ignore hook, fired for the unit that just shrugged the wounds off.
                // Declared as EHookID.Lifecycle_OnWoundIgnored since #042 but never lit until now, so a
                // rule authored here used to validate, lint clean and do nothing. Regenerative Strength's
                // marker is the one reader: its value is `ignored`, which is fractional under the
                // probabilistic roller and whole under the realistic one.
                //
                // Guarded on ignored > 0f so the hook never fires as a no-op - IHasIgnoredWoundCount
                // promises a positive count, which is what lets rules here skip the empty-firing guard.
                // Token operations only: this is mid-wound-resolution, so nothing here may execute (a
                // move, a spawn) or prompt. GrantIgnoredWoundMarker emits exactly one grant.
                if (ignored > 0f)
                {
                    IReadOnlyList<RuleOperation> ignoredWoundOperations = GameContext.RuleEvaluator.EvaluateAll(
                        new WoundIgnoredContext(defender, attacker, ignored),
                        // Subject seat, models passed for the same reason as the save-complete evaluation
                        // above: a joined hero's relocated per-model rule must still be seen.
                        RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));
                    OperationApplier.ApplyTokenOperations(ignoredWoundOperations);

                    // Self-attributing log, the Sergeant precedent: a marker that accrues silently is
                    // indistinguishable in play from one that never accrued, and this is the seam where a
                    // regression would hide. Names the rule because it is the hook's only reader and the
                    // read side is already rule-named stage code (RegenerativeStrengthAttacks); a second
                    // reader here would mean generalizing this line, not keeping it vague now.
                    if (ignoredWoundOperations.Count > 0)
                    {
                        GameContext.Log($"{defender.Name} banks {ignored:0.##} Regenerative Strength " +
                            $"marker(s) - total " +
                            $"{defender.Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker):0.##}.");
                    }
                }
            }

            // #042 Takedown: if the attack was re-scoped to a single model (IndividualTargetResult,
            // produced by BuildTargetListStage), all wounds funnel to that one model — capped at its
            // remaining wounds, no carry-over to the rest of the unit ("resolve as a unit of [1]"). This
            // bypasses the normal allocation branches (which spread across, or kill, the whole unit).
            if (metaData.QueryForResult(out IndividualTargetResult individualTarget))
            {
                float modelRemaining = individualTarget.Model.GetValue().RemainingWoundsBinding.GetValue();
                float confined = Math.Min(totalWoundsDealt, modelRemaining);
                AssignWoundsResults takedownResults = new AssignWoundsResults(individualTarget.Model, confined);
                takedownResults.AutoFill();
                if (confined > 0f)
                {
                    GameContext.Log($"{individualTarget.SourceLabel} assigned {confined} wound(s) to the single targeted model.");
                }
                await onFinished(takedownResults);
                return;
            }

            float defenderRemainingWounds = metaData.DefendingUnit.RemainingWounds();

            //If the opponent doesn't have to provide a choice, like if the unit will die or there's just one model,
            //then just do it automatically.
            AssignWoundsResults assignWoundsResults;

            if(totalWoundsDealt == 0)
            {
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, 0);
                //Should be auto-filled regardless but just do it. 
                assignWoundsResults.AutoFill();
            }
            else if (totalWoundsDealt >= defenderRemainingWounds)
            {
                //We've killed off the unit. No need to use the handler to ask what will die.
                //Fill results with wounds it would take to kill.
                //TODO: Would be cool to list overkill amount somewhere besides text log.
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, defenderRemainingWounds);
                assignWoundsResults.AutoFill();

                float overkill = totalWoundsDealt - defenderRemainingWounds;
                string pluralizedWound = defenderRemainingWounds == 1 ? "wound" : "wounds";
                GameContext.Log($"Assigning {defenderRemainingWounds} {pluralizedWound} (Overkill: {overkill})");
            }
            else if (metaData.DefendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .Count() == 1)
            {
                //If we only have one living model there's no allocation choice, so auto-resolve it — but
                //assign the wounds actually DEALT, not the model's full remaining health. (A single
                //multi-wound model, e.g. Tough, otherwise got auto-killed by a sub-lethal hit. We're past
                //the totalWoundsDealt >= remaining branch, so totalWoundsDealt < remaining and AutoFill fits.)
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, totalWoundsDealt);
                assignWoundsResults.AutoFill();
            }
            else
            {
                // Construct the results up front so the mandatory Tough pre-assignment (already-wounded
                // models filled first, non-cancellable) is applied before we decide whether the player
                // still has anything to choose. If the pre-assignment consumed the pool — or left only a
                // single eligible model — there's no decision to make, so resolve without prompting.
                AssignWoundsResults trial = new AssignWoundsResults(metaData.DefendingUnit, totalWoundsDealt);
                if (trial.HasRemainingChoice)
                {
                    AssignWoundsRequest request = new AssignWoundsRequest(metaData.DefendingUnit.PlayerID(),
                        "Assigning Wounds", metaData.DefendingUnit, totalWoundsDealt);
                    assignWoundsResults = await metaData.GameContext.PlayerRequester()
                        .RequestDecision<AssignWoundsRequest, AssignWoundsResults>(request);
                }
                else
                {
                    trial.AutoFill();
                    assignWoundsResults = trial;
                }
            }

            await onFinished(assignWoundsResults);
        }

        // Deadly's no-carry-over confinement. The attack landed <paramref name="clumpCount"/> failed
        // saves; under Deadly(X) each is a clump of X wounds confined to one model, with any overkill on
        // that model lost rather than carrying to the next. Walks the defender's living models in order,
        // assigning whole clumps until each model is dead (ceil(capacity / X) clumps), and sums the wounds
        // that actually land (a clump on a model deals min(X, that model's remaining), so a 1-wound model
        // absorbs only 1 of the X). Returns the effective wound total.
        //
        // Model ORDER here is the unit's model list (matching AutoFill); the defender's freedom to choose
        // which models absorb clumps to minimise casualties is the same tough-aware allocation choice the
        // player branch below still defers. Against single-wound models order is irrelevant (every clump
        // kills exactly one), which is the common case and the headline fix over the old total*X.
        private static float ConfineToClumps(float clumpCount, int multiplier, IUnit defender)
        {
            float effective = 0f;
            float remainingClumps = clumpCount;

            foreach (IModel model in defender.Models)
            {
                if (remainingClumps <= 0f) break;
                if (!model.GetIsAlive()) continue;

                float capacity = model.TotalWounds - model.WoundsDealt;
                if (capacity <= 0f) continue;

                float clumpsToKill = MathF.Ceiling(capacity / multiplier);
                float used = MathF.Min(remainingClumps, clumpsToKill);
                effective += MathF.Min(used * multiplier, capacity);
                remainingClumps -= used;
            }

            return effective;
        }

        // Reconstructs the full unmodified save-roll histogram from the failed + successful subsets,
        // so SaveRollCompleteContext carries the real rolls. Regeneration reads nothing from them
        // (Condition.Always), but a future Bane re-rolling unmodified Defense 6s needs At(6) accurate,
        // so build it honestly now. Saves are d6.
        private static IDiceResults CombineSaveRolls(RollToSaveResults saves)
        {
            float[] perFace = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];

            foreach (SuccessfulSaveInfo successful in saves.SuccessfulSaveList)
            {
                AccumulateFaces(successful.Rolls, perFace);
            }
            foreach (FailedSaveInfo failed in saves.FailedSaveList)
            {
                AccumulateFaces(failed.Rolls, perFace);
            }

            return new DiceResults(perFace);
        }

        private static void AccumulateFaces(IDiceResults rolls, float[] perFace)
        {
            for (int face = rolls.SideMin; face <= rolls.SideMax && face <= perFace.Length; face++)
            {
                perFace[face - 1] += rolls.At(face);
            }
        }

        /*
        private void OnHandled(AssignWoundsResults woundsResults, Action<AssignWoundsResults> onFinished)
        {
            if (woundsResults.IsFinishedAssigning == false)
            {
                throw new InvalidOperationException($"Called assigning wounds finished when it was not finished. " +
                    $"Wounds to assign: {woundsResults.TotalWoundsToAssign} Wounds assigned: {woundsResults.TotalAssignedWounds}.");
            }

            onFinished(woundsResults);
        }
        */
    }
}