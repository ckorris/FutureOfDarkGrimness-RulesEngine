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

        protected override async Task RunStage(ICombatMetadata metaData, Action<AssignWoundsResults> onFinished)
        {
            metaData.QueryForResult(out RollToSaveResults rollToSaveResults);

            float totalWoundsDealt = 0;
            foreach (FailedSaveInfo failedSaves in rollToSaveResults.FailedSaveList)
            {
                totalWoundsDealt += failedSaves.SaveCount;
            }

            // #042 wound-multiplier rules (Deadly) fire at pre-apply-wound: evaluate the attacker's
            // rules, fold MultiplyWounds ops through the sink, and scale the wound count. The stage
            // interprets no operation; it just reads the net multiplier.
            // SCOPE: this multiplies the TOTAL wound count (faithful for total damage). Deadly's
            // per-wound / single-model / no-carry-over allocation nuance is Phase-8 behavior, tracked
            // by the tough-aware allocation TODO below.
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            IReadOnlyList<RuleOperation> woundOperations = GameContext.RuleEvaluator.EvaluateAll(
                new PreApplyWoundContext(attacker, defender),
                (attacker, ERuleSeat.Actor));
            WoundModifierSink woundModifier = new WoundModifierSink();
            woundModifier.ApplyFrom(woundOperations);
            totalWoundsDealt *= woundModifier.NetMultiplier;

            // #042 wound-ignore rules (Regeneration) fire at save-roll-complete: the defender ignores
            // each wound on a roll of X+. Evaluate BOTH participants so an attacker suppressor
            // (Bane / Rending / Unstoppable "ignore Regeneration") can cancel it via the evaluator's
            // suppression first-pass; fold the ignore sink, then roll one d6 per wound at the best
            // threshold and drop the ignored count. The stage interprets no operation — it reads only
            // the threshold and rolls.
            // ORDER: applied AFTER Deadly's multiply (the rulebook tags Deadly "resolved first"). The
            // per-wound / single-model allocation nuance of both rules stays Phase-8 (tough-aware TODO below).
            IReadOnlyList<RuleOperation> saveCompleteOperations = GameContext.RuleEvaluator.EvaluateAll(
                new SaveRollCompleteContext(attacker, defender, CombineSaveRolls(rollToSaveResults)),
                (attacker, ERuleSeat.Actor),
                (defender, ERuleSeat.Subject));
            WoundIgnoreSink woundIgnore = new WoundIgnoreSink();
            woundIgnore.ApplyFrom(saveCompleteOperations);
            if (woundIgnore.HasIgnore && totalWoundsDealt > 0f)
            {
                float ignored = GameContext.DiceRoller.Roll(totalWoundsDealt).AtOrAbove(woundIgnore.Threshold);
                totalWoundsDealt -= ignored;
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

            onFinished(assignWoundsResults);
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