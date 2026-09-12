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
            // rules, fold MultiplyWounds ops through the sink, and read the net multiplier. The stage
            // interprets no operation.
            IReadOnlyList<RuleOperation> woundOperations = GameContext.RuleEvaluator.EvaluateAll(
                new PreApplyWoundContext(attacker, defender),
                RuleParticipant.Actor(attacker, metaData.WeaponType));
            WoundModifierSink woundModifier = new WoundModifierSink();
            woundModifier.ApplyFrom(woundOperations);

            // #401: from here on the wounds are a queue of packets, not a number. Deadly(X) turns each
            // failed save into a confined clump of X that lands entirely on ONE model and does NOT carry
            // over - so the multiplier is wasted against single-wound models (Deadly's whole point is
            // anti-Tough) and a clump's excess past a Tough model is lost. Without Deadly the queue is the
            // one unconfined pool it always was. WHICH model each packet reaches is AssignWoundsResults'
            // business (already-wounded first, hero last, finish a model before starting another); this
            // stage only decides what is in the queue.
            List<WoundPacket> packets = WoundAllocation.Packets(totalWoundsDealt, woundModifier.NetMultiplier);
            if (woundModifier.NetMultiplier > 1 && packets.Count > 0)
            {
                GameContext.Log($"{totalWoundsDealt:0.##} failed save(s) become clump(s) of " +
                    $"{woundModifier.NetMultiplier} wounds, each confined to one model.");
            }

            // #100 Shred (wound injection): "for each unmodified 1 to block, +1 wound" fires at
            // save-complete, so it rides the same saveCompleteOperations queue. Fold the wound-injection
            // sink and append the extra wounds as their own plain pool - AFTER the Deadly clumps (the
            // rulebook resolves Deadly first; Shred + Deadly on one weapon isn't in the corpus anyway) and
            // BEFORE Regeneration, so the defender may still ignore the Shred wounds like any others.
            WoundInjectionSink woundInjection = new WoundInjectionSink();
            woundInjection.ApplyFrom(saveCompleteOperations);
            if (woundInjection.TotalExtraWounds > 0f)
            {
                packets.Add(WoundPacket.Unconfined(woundInjection.TotalExtraWounds));
                GameContext.Log($"Shred added {woundInjection.TotalExtraWounds:0.##} extra wound(s).");
            }

            // #376 Bloodthirsty Fighter (bonus attacks): "each unmodified 1 to block earns a follow-up
            // attack with this weapon" also rides the save-complete queue. Fold the sum and post it for
            // ResolveBonusMeleeAttacksStage (melee swing chain only; other pipelines drop it, the
            // DealAutoWounds doctrine). Never posted for a bonus batch itself - no chaining.
            BonusAttackSink bonusAttacks = new BonusAttackSink();
            bonusAttacks.ApplyFrom(saveCompleteOperations);
            if (bonusAttacks.TotalBonusAttacks > 0f && !metaData.IsBonusAttack)
            {
                metaData.AddResult(new BonusAttackResults(bonusAttacks.TotalBonusAttacks));
                GameContext.Log($"Block rolls of 1 earn {attacker.Name} " +
                    $"{bonusAttacks.TotalBonusAttacks:0.##} follow-up attack(s).");
            }

            // #042 wound-ignore rules (Regeneration) from the same save-complete queue: the defender
            // ignores each wound on a roll of X+. Fold the ignore sink, then roll one d6 per wound at the
            // best threshold. The stage interprets no operation.
            //
            // #401: rolled PER PACKET, before any capacity cap - "you have to slow-roll the Regeneration
            // saves" (the rule's author, OPR Discord 2026-07-22). A Deadly(3) clump on a 1-wound model
            // is three wounds arriving at that model, so it gets three chances to shrug and survives only
            // if it ignores all three; and what a clump loses to Regeneration is lost to THAT clump, on
            // THAT model - it never frees up budget that reaches the next one. Rolling clump k's dice
            // before the player picks its target is statistically identical to rolling after, because
            // the sink folds every ignore source to one unit-wide threshold; that is what lets this stay
            // one request and one dialog. ORDER: after Deadly's multiply (the rulebook tags Deadly
            // "resolved first").
            WoundIgnoreSink woundIgnore = new WoundIgnoreSink();
            woundIgnore.ApplyFrom(saveCompleteOperations);
            if (woundIgnore.HasIgnore && packets.Count > 0)
            {
                float ignoredTotal = 0f;
                int clumpCount = packets.Count(packet => packet.Confined);
                int clumpIndex = 0;
                for (int i = 0; i < packets.Count; i++)
                {
                    WoundPacket packet = packets[i];
                    if (packet.Confined) clumpIndex++;
                    IDiceResults regenRoll = GameContext.DiceRoller.Roll(packet.Wounds);
                    float ignored = regenRoll.AtOrAbove(woundIgnore.Threshold);
                    packets[i] = packet.WithWounds(packet.Wounds - ignored);
                    ignoredTotal += ignored * packet.Weight;
                    string context = packet.Confined && clumpCount > 1
                        ? $"{defender.Name} - clump {clumpIndex} of {clumpCount}"
                        : defender.Name;
                    await GameContext.Presenter.Present(DiceRolledBeat.From(regenRoll, woundIgnore.Threshold,
                        GameContext.Settings.RandomnessType, "Regeneration", $"{ignored:0.##} ignored",
                        category: ERollBeatCategory.Defense, context: context));
                }

                // #197 P12: the wound-ignore hook, fired for the unit that just shrugged the wounds off.
                // Declared as EHookID.Lifecycle_OnWoundIgnored since #042 but never lit until now, so a
                // rule authored here used to validate, lint clean and do nothing. Regenerative Strength's
                // marker is the one reader: its value is the ignored total, which is fractional under the
                // probabilistic roller and whole under the realistic one.
                //
                // Guarded on ignoredTotal > 0f so the hook never fires as a no-op - IHasIgnoredWoundCount
                // promises a positive count, which is what lets rules here skip the empty-firing guard.
                // Token operations only: this is mid-wound-resolution, so nothing here may execute (a
                // move, a spawn) or prompt. GrantIgnoredWoundMarker emits exactly one grant.
                if (ignoredTotal > 0f)
                {
                    IReadOnlyList<RuleOperation> ignoredWoundOperations = GameContext.RuleEvaluator.EvaluateAll(
                        new WoundIgnoredContext(defender, attacker, ignoredTotal),
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
                        GameContext.Log($"{defender.Name} banks {ignoredTotal:0.##} Regenerative Strength " +
                            $"marker(s) - total " +
                            $"{defender.Tokens.GetTokenMagnitude(TokenType.RegenerativeStrengthMarker):0.##}.");
                    }
                }
            }

            // #042 Takedown: if the attack was re-scoped to a single model (IndividualTargetResult,
            // produced by BuildTargetListStage), every packet funnels to that one model, no carry-over to
            // the rest of the unit ("resolve as a unit of [1]"); what it cannot absorb is lost. This
            // bypasses the normal allocation below (which spreads across, or kills, the whole unit).
            if (metaData.QueryForResult(out IndividualTargetResult individualTarget))
            {
                AssignWoundsResults takedownResults = new AssignWoundsResults(individualTarget.Model, packets);
                takedownResults.AutoFill();
                if (takedownResults.TotalAssignedWounds > 0f)
                {
                    GameContext.Log($"{individualTarget.SourceLabel} assigned " +
                        $"{takedownResults.TotalAssignedWounds:0.##} wound(s) to the single targeted model.");
                }
                LogLostWounds(takedownResults);
                await onFinished(takedownResults);
                return;
            }

            // Construct the results up front so the mandatory Tough pre-assignment (already-wounded
            // models filled first, non-cancellable) is applied before we decide whether the player still
            // has anything to choose. No prompt when nothing is queued, the pre-assignment consumed it,
            // only one model could take it, or every legal order kills the whole unit anyway - the player
            // is asked exactly when the answer can differ.
            AssignWoundsResults assignWoundsResults = new AssignWoundsResults(metaData.DefendingUnit, packets);
            if (assignWoundsResults.HasRemainingChoice && !assignWoundsResults.AutoFillWouldKillEveryModel())
            {
                AssignWoundsRequest request = new AssignWoundsRequest(metaData.DefendingUnit.PlayerID(),
                    "Assigning Wounds", metaData.DefendingUnit, packets);
                assignWoundsResults = await metaData.GameContext.PlayerRequester()
                    .RequestDecision<AssignWoundsRequest, AssignWoundsResults>(request);
            }
            else
            {
                assignWoundsResults.AutoFill();
            }

            LogLostWounds(assignWoundsResults);
            await onFinished(assignWoundsResults);
        }

        // Wounds that never landed are worth a line each: overkill past a dead unit was always logged,
        // and a Deadly clump's excess past its model is the rule working - which a player used to the
        // old spill-over would otherwise read as wounds gone missing.
        private void LogLostWounds(AssignWoundsResults results)
        {
            if (results.Overkill > AssignWoundsResults.WoundEpsilon)
            {
                GameContext.Log($"Assigning {results.TotalAssignedWounds:0.##} wound(s) " +
                    $"(Overkill: {results.Overkill:0.##})");
            }
            if (results.ClumpExcessLost > AssignWoundsResults.WoundEpsilon)
            {
                GameContext.Log($"{results.ClumpExcessLost:0.##} wound(s) lost - a Deadly clump does not " +
                    "carry over past the model it hit.");
            }
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