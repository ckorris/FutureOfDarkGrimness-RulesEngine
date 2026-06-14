using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
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

            // #042 save-roll-complete rules (Bane reroll, Regeneration ignore, plus the suppressors'
            // ignore-Regeneration facet) all fire here. Evaluate BOTH participants once, so the
            // evaluator's suppression first-pass can cancel Regeneration before its op is folded. The
            // resulting queue feeds the reroll (below, before Deadly) and the wound-ignore (after Deadly).
            IReadOnlyList<RuleOperation> saveCompleteOperations = GameContext.RuleEvaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker, defender, CombineSaveRolls(rollToSaveResults)),
                (attacker, ERuleSeat.Actor, metaData.WeaponType),
                (defender, ERuleSeat.Subject, null));

            // #042 save-reroll rules (Bane): the defender re-rolls unmodified-6 saves, turning saved 6s
            // into possible failures. Re-roll each successful group's natural-6 count and add the new
            // failures to the wound total — done BEFORE Deadly multiplies, since it finalizes the saves.
            RerollSink rerollSink = new RerollSink();
            rerollSink.ApplyFrom(saveCompleteOperations);
            if (rerollSink.RerollSavesOnUnmodifiedMax)
            {
                foreach (SuccessfulSaveInfo saved in rollToSaveResults.SuccessfulSaveList)
                {
                    float naturalMax = saved.Rolls.At(saved.Rolls.SideMax);
                    if (naturalMax <= 0f) continue;
                    int saveNeeded = DiceUtilities.ClampSuccessRollNeeded(saved.RollNeededInfo.SaveNeeded);
                    totalWoundsDealt += GameContext.DiceRoller.Roll(naturalMax).Below(saveNeeded);
                }
            }

            // #042 wound-multiplier rules (Deadly) fire at pre-apply-wound: evaluate the attacker's
            // rules, fold MultiplyWounds ops through the sink, and scale the wound count. The stage
            // interprets no operation; it just reads the net multiplier.
            IReadOnlyList<RuleOperation> woundOperations = GameContext.RuleEvaluator.EvaluateAll(
                new PreApplyWoundContext(attacker, defender),
                (attacker, ERuleSeat.Actor, metaData.WeaponType));
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

            // #042 wound-ignore rules (Regeneration) from the same save-complete queue: the defender
            // ignores each wound on a roll of X+. Fold the ignore sink, then roll one d6 per wound at the
            // best threshold and drop the ignored count. The stage interprets no operation.
            // ORDER: applied AFTER Deadly's multiply (the rulebook tags Deadly "resolved first"). The
            // per-wound / single-model allocation nuance of both rules stays Phase-8 (tough-aware TODO below).
            WoundIgnoreSink woundIgnore = new WoundIgnoreSink();
            woundIgnore.ApplyFrom(saveCompleteOperations);
            if (woundIgnore.HasIgnore && totalWoundsDealt > 0f)
            {
                float ignored = GameContext.DiceRoller.Roll(totalWoundsDealt).AtOrAbove(woundIgnore.Threshold);
                totalWoundsDealt -= ignored;
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
                    GameContext.Log($"Takedown assigned {confined} wound(s) to the single targeted model.");
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
                //If we only have one living model, just autoresolve it.
                assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, defenderRemainingWounds);
                assignWoundsResults.AutoFill();
            }
            else
            {
                //TODO: Add nuance of applying wounds to existing models with tough before others.
                //I'm also putting this TODO in the results class.
                //assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, totalWoundsDealt);
                AssignWoundsRequest request = new AssignWoundsRequest(metaData.DefendingUnit.PlayerID(), "Assign Wounds", 
                    metaData.DefendingUnit, totalWoundsDealt);
                assignWoundsResults = await metaData.GameContext.PlayerRequester()
                    .RequestDecision<AssignWoundsRequest, AssignWoundsResults>(request);
                //throw new NotImplementedException();
                //GameContext.GetHandler<IAssignWoundsHandler>().Handle(metaData.DefendingUnit, assignWoundsResults, () => OnHandled(assignWoundsResults, onFinished));
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