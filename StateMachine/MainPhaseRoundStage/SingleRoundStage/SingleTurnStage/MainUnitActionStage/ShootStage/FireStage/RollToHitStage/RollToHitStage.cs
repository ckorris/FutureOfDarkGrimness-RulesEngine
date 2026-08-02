using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{

    public class RollToHitStage<TMetadata> : CombatStage<RollToHitResults, RollToHitStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public RollToHitStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<RollToHitResults, Task> onFinished)
        {
            // Attack count and hit threshold are both determined in DetermineHitRollStage (#015).
            // Read them here and roll the determined AttackCount — not a local product — so any
            // attack-count modifier that stage folds in is honoured. Rolled BEFORE the attack beat
            // (#239) so the beat can carry the true hit share; the roll's position in the RNG
            // sequence is unchanged.
            DetermineHitRollResults hitRollResults = QueryForResultOrThrowException<DetermineHitRollResults>(metaData);

            float attacks = hitRollResults.AttackCount;
            IDiceResults rollToHitResults = GameContext.DiceRoller.Roll(attacks);

            //We do this here because modifiers shouldn't do it, or else they can't add up in opposite
            //directions. For example, if your Quality is 6, and something gives you +1 to hit, and something
            //else gives you -1 to hit. They should cancel each other out and you'd need a 6. But if you processed
            //the +1 first, and clamped it, it would still be 6, and the -1 would move it to 5.

            int hitRollNeeded = DiceUtilities.ClampSuccessRollNeeded(hitRollResults.HitRollNeeded);

            IDiceResults successfulResults = rollToHitResults.SubsetAtOrAbove(hitRollNeeded);
            IDiceResults failedResults = rollToHitResults.SubsetBelow(hitRollNeeded);

            // Show the attack — tracers (ranged) or a clash (melee) — playing WHILE the dice that
            // resolve it tumble (#238). #276: endpoints are truthful — shots fire only from carriers
            // that can actually strike (LoS + range / melee range), capped at the rolled weapon count
            // (a #157 split shot fires ONE beam, from a different sniper each shot), and a Takedown
            // shot's tracer aims at its picked model.
            (List<Position> attackerPositions, List<Position> targetPositions) =
                AttackBeatPositions.Endpoints(GameContext.TableState, metaData, GameContext.RuleEvaluator);
            if (attackerPositions.Count > 0 && targetPositions.Count > 0)
            {
                // Each volley fires every weapon at once; the weapon's Attacks is the volley count.
                // #239: the weapon's effect key + the natural hit share ride along so the front-end
                // picks the right visual/sounds and lands only the shots that hit.
                await GameContext.Presenter.Present(new AttackBeat(metaData.IsMelee,
                    attackerPositions, targetPositions,
                    volleyCount: metaData.WeaponType.Attacks,
                    armorPenetration: metaData.WeaponType.ArmorPenetration,
                    weaponEffect: metaData.WeaponType.EffectKey,
                    hitCount: successfulResults.TotalRolls,
                    attackCount: attacks));
            }

            GameContext.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            // #042 hit-roll-complete rules fire here, evaluated against the UNMODIFIED rolls (the synthetic
            // hits added below sit at face 6 and would pollute a natural-6 read). One evaluation feeds them
            // all: extra-hit (Surge/Furious/Relentless), hit-multiplier (Blast), per-hit-AP (Rending/Crack)
            // and whole-attack save (Thrust) rules on the attacker, plus the defender's defensive save mods.
            // #245: evaluated BEFORE the dice beat is presented (it's a pure computation - no awaits, no
            // rolls - so the RNG sequence and grant spends are unchanged) so the beat can carry the
            // face-triggered proc chips ("Furious +2 on 6s") that this evaluation produces.
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);

            // #204: the LIVE named evaluation - spends grants / narrates exactly like EvaluateAll, but also
            // pairs each op with the alias-aware rule name so the save presentation can say "+1 (Furious)",
            // "x3 (Blast)", "Rending AP+1". `operations` is the plain-op view the existing sinks consume.
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new HitRollCompleteContext(attacker, defender, rollToHitResults, distance, metaData.IsMelee,
                    metaData.IsCharging, IsSpell: false,
                    ChargeOriginDistanceInches: metaData.ChargeOriginDistanceInches,
                    UnpredictableBranch: metaData.UnpredictableBranch,
                    // #197 misc (Grounded family): the terrain-proximity conditions read the bearer's
                    // models against the live terrain layout.
                    TerrainPieces: GameContext.TableState.Terrain.Objects.ToList()),
                // #006 slice F / #093: the attacker batch's living owners contribute their per-model rules
                // under AllOwners semantics (fires only when every owner shares it), so a joined hero's
                // Furious/Relentless fire for a hero-only batch and a homogeneous squad's shared per-model
                // rule fires once — without leaking onto a mixed batch's pooled roll.
                RuleParticipant.Actor(attacker, metaData.WeaponType,
                    HeroStatRules.LivingWeaponBatchOwners(metaData.AttackingUnit.GetValue(), metaData.WeaponType),
                    EModelRuleScope.AllOwners),
                // The defender contributes its DEFENSIVE save modifiers here (Shielded's +1 to defense,
                // Fortified's AP reduction) — the mirror of how DetermineHitRollStage evaluates the defender
                // as Subject for hit modifiers. Its Net(Save) folds into RollToHitResults.SaveModifier below
                // alongside the attacker's whole-attack AP, so a defensive +1 and an attacker's -N net
                // correctly. #183: passing the defender's living models surfaces a joined hero's relocated
                // Shielded/Fortified (gated by AllModelsHaveThisRule) instead of silently dropping them.
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));

            IReadOnlyList<RuleOperation> operations = named.Select(n => n.Op).ToList();

            // Show the natural to-hit roll (the synthetic extra-hits below aren't dice), enriched with
            // the #245 glance metadata: who is rolling at whom, how the threshold came to be (chips
            // composed in DetermineHitRollStage), and which face-triggered rules fired on it.
            List<string> procTags = ComposeProcTags(named);
            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rollToHitResults, hitRollNeeded, GameContext.Settings.RandomnessType,
                    HitBeatLabel(metaData.WeaponType),
                    $"{successfulResults.TotalRolls:0.##} hits",
                    category: ERollBeatCategory.Offense,
                    context: HitBeatContext(attacker.Name, defender.Name, attacks),
                    modifierTags: hitRollResults.ThresholdTags,
                    procTags: procTags.Count > 0 ? procTags : null));

            // #032 per-hit AP (Rending/Crack on an unmodified 6): split the rolled successes so only the
            // matching-face hits carry the raised save threshold; the rest stay at base AP. With no such
            // rule the splitter returns a single group — identical to the old one-group behaviour. The named
            // form (#204) tags each peeled group with its rule so the save stage can say "Rending AP+1".
            RollToHitResults results = new RollToHitResults(
                PerHitApSplitter.Split(successfulResults, named),
                new List<FailedHitInfo>() { new FailedHitInfo(failedResults) });

            // #042 extra-hit rules (Surge / Furious / Relentless) fire at hit-roll-complete: an unmodified 6
            // spawns extra hits. Fold InsertExtraHits ops through the sink and append the total as ONE
            // synthetic group (base AP — a generated hit is not itself a natural 6). #204 keeps this ONE-group
            // structure byte-for-byte (so the save-roll RNG is untouched and outcomes don't move) and only
            // TAGS the group with the responsible rule name(s) for the save presentation ("+3 (Furious)").
            HitInjectionSink hitInjection = new HitInjectionSink();
            hitInjection.ApplyFrom(operations);
            if (hitInjection.TotalExtraHits > 0f)
            {
                results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                    SyntheticHits(hitInjection.TotalExtraHits, rollToHitResults), 0,
                    new HitGroupSource(EHitSourceKind.ExtraHits, ExtraHitRuleNames(named), hitInjection.TotalExtraHits)));
            }

            // #042 hit-multiplier rules (Blast) fire at the same hook but resolve "after other rules":
            // multiply the POST-injection hit total. Folded after the injection above so Blast multiplies
            // whatever hits landed (including Surge's).
            //
            // The model-count cap is PER HIT, and the multiplied hits STACK across hits (owner-ruled
            // 2026-07-31): "hits X times for each hit, but no more times than there are models in the
            // target unit" bounds what ONE hit can fan out to, not the volley's total. So an A3 Blast(3)
            // that lands 3 hits on a 3-model unit deals 3 x 3 = 9, and on a 2-model unit 3 x 2 = 6 - not
            // 3 either way, which is what capping the TOTAL used to produce (and which silently deleted
            // save dice the defender owed).
            HitMultiplierSink hitMultiplier = new HitMultiplierSink();
            hitMultiplier.ApplyFrom(operations);
            if (hitMultiplier.NetMultiplier > 1)
            {
                float currentHits = TotalHits(results);
                int targetModelCount = CountLivingModels(defender);
                // Floored at 1 so a target with no living models (unreachable in play - such a unit is not
                // a legal target) leaves the hits untouched rather than erasing them.
                int effectiveMultiplier = Math.Max(1, Math.Min(hitMultiplier.NetMultiplier, targetModelCount));
                float cappedHits = currentHits * effectiveMultiplier;
                float extraHits = cappedHits - currentHits;
                if (extraHits > 0f)
                {
                    // #204: tag the overflow group so the save stage shows "xN (Blast)" (name from the op,
                    // falling back to "Blast" if a book aliased it away). The EFFECTIVE multiplier rides
                    // along, not the authored one, so the beat's arithmetic ("3 hits x2 (Blast) = 6") adds
                    // up when the target's model count trimmed it.
                    string blastName = named.FirstOrDefault(n => n.Op is RuleOperation.MultiplyHits).RuleName;
                    if (string.IsNullOrEmpty(blastName)) blastName = "Blast";
                    results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                        SyntheticHits(extraHits, rollToHitResults), 0,
                        new HitGroupSource(EHitSourceKind.BlastMultiplier, blastName, effectiveMultiplier)));
                    GameContext.Log($"Blast multiplied {currentHits} hits x{effectiveMultiplier} " +
                        $"(authored x{hitMultiplier.NetMultiplier}, capped per hit at {targetModelCount} " +
                        $"target model(s)) -> {cappedHits} total.");
                }
            }

            // Whole-attack save-modifier rules fold their net modifier here and carry it to the save stage
            // via RollToHitResults.SaveModifier — Thrust's charge AP, the defender's Shielded +1. Rending
            // and Crack no longer ride this scalar: their AP is per-hit (split into a face group above), so
            // this now holds only genuinely attack-wide modifiers.
            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(operations);
            results.SaveModifier = saveModifiers.Net(ERollKind.Save);

            // Fortified (defender) reduces the incoming WEAPON AP, floored at the save stage. Sum any
            // reduction ops here and carry the total alongside the save modifier.
            results.ArmorPenetrationReduction = operations
                .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            // #245: name the rules behind the two scalars above so the save beat's chips can say
            // "Shielded +1" / "Thrust -1" / "Fortified AP-1" instead of an anonymous net number.
            results.SaveModifierTags = ComposeSaveModifierTags(named);

            // #197 Hazardous: the attacker's own unmodified 1s wound it. Counted here, where the UNMODIFIED
            // histogram is in hand, and CARRIED to ApplyWoundsStage - the last stage of every chain that
            // has a hit roll (shooting, melee swings, Strafing) - which applies it once the target's wounds
            // have landed. Owner-ruled 2026-07-29: the shot the models paid for resolves first, and the
            // attacking unit is never torn down while later stages of its own attack are still running.
            // The player learns of it at the moment of the roll, via the proc chip on the to-hit beat above.
            results.SelfWounds = operations.OfType<RuleOperation.InflictSelfWounds>().Sum(op => op.Wounds);

            await onFinished(results);
        }

        // The to-hit beat's header names the weapon being rolled with, mirroring how the save beat is
        // captioned "{weapon}: {breakdown}" (#204) — with several weapon batches firing in one activation
        // the bare "Roll to Hit" said nothing about which one this was. Falls back to the bare label for a
        // nameless weapon. Internal for tests.
        internal static string HitBeatLabel(IWeapon weapon) =>
            string.IsNullOrWhiteSpace(weapon.Name) ? "Roll to Hit" : $"Roll to Hit - {weapon.Name}";

        // The context line: who is rolling at whom, plus the size of the volley. The count is the TOTAL
        // attacks rolled (carriers x the weapon's Attacks, plus any attack-count modifier), so it matches
        // the dice on screen — and it is the only readable count under the probabilistic roller, which
        // draws a success bar instead of dice. Internal for tests.
        internal static string HitBeatContext(string attackerName, string defenderName, float attacks) =>
            $"{attackerName} -> {defenderName}  |  {attacks:0.##} attack{(attacks == 1f ? "" : "s")}";

        // #245: the to-hit beat's gold proc chips - face-triggered rule effects that fired on this roll.
        // Extra hits (Furious/Surge/Relentless) trigger on the top face; per-hit AP (Rending/Crack)
        // names its own face. The front-end highlights top-face successes when any chip is present.
        // Internal for tests.
        internal static List<string> ComposeProcTags(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            List<string> tags = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                switch (op)
                {
                    case RuleOperation.InsertExtraHits extra when extra.Count > 0f:
                        tags.Add($"{RollTags.NameOr(ruleName, "extra hits")} +{extra.Count:0.##} on 6s");
                        break;
                    case RuleOperation.ApplyPerHitSaveModifier perHit when perHit.Delta != 0:
                        // A negative save delta raises the threshold - display as the AP it plays as.
                        tags.Add($"{RollTags.NameOr(ruleName, "AP")} AP+{-perHit.Delta:0.##} on {perHit.OnRollValue}s");
                        break;
                    // #197 Hazardous: the self-wound is applied after the attack resolves, but it is the
                    // 1s on THIS roll that caused it, so the chip belongs on this beat.
                    case RuleOperation.InflictSelfWounds self when self.Wounds > 0f:
                        tags.Add($"{RollTags.NameOr(ruleName, "self-wound")} {self.Wounds:0.##} " +
                            $"self-wound{(self.Wounds == 1f ? "" : "s")}");
                        break;
                }
            }
            return tags;
        }

        // #245: chips naming the whole-attack save modifiers this evaluation produced, for the save
        // beat. Positive is the defender's favor (Shielded +1), negative the attacker's (Thrust -1);
        // Fortified's AP reduction reads as the AP it removes. Internal for tests.
        internal static List<string>? ComposeSaveModifierTags(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            List<string> tags = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                switch (op)
                {
                    case RuleOperation.ApplyRollModifier { Roll: ERollKind.Save } mod when mod.Delta != 0:
                        tags.Add($"{RollTags.NameOr(ruleName, "modifier")} {RollTags.Delta(mod.Delta)}");
                        break;
                    case RuleOperation.ReduceArmorPenetration reduce when reduce.Amount != 0:
                        tags.Add($"{RollTags.NameOr(ruleName, "AP reduction")} AP-{reduce.Amount}");
                        break;
                }
            }
            return tags.Count > 0 ? tags : null;
        }

        // #204: the distinct rule name(s) that added on-6 extra hits, joined for the save presentation
        // (usually one, e.g. "Furious"; "Furious, Surge" if two stack). Presentation only - does not affect
        // the pooled hit total. Empty rule names fall back to "extra hits".
        private static string ExtraHitRuleNames(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            var names = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.InsertExtraHits extra || extra.Count <= 0f) continue;
                string name = string.IsNullOrEmpty(ruleName) ? "extra hits" : ruleName;
                if (!names.Contains(name)) names.Add(name);
            }
            return names.Count > 0 ? string.Join(", ", names) : "extra hits";
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
            }
            return total;
        }

        private static int CountLivingModels(IUnit unit)
        {
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) count++;
            }
            return count;
        }

        // Bridges a scalar extra-hit count into the IDiceResults the save flow consumes. Injected
        // hits have no real face — only the count (TotalRolls) matters downstream, plus the weapon's
        // AP — so they sit at the top face as automatic successes. The #032 per-hit AP split
        // (Rending/Crack) runs only over the ROLLED successes, never these synthetic groups, so injected
        // hits stay base-AP even though they sit at face 6 — exactly as they must.
        private static IDiceResults SyntheticHits(float count, IDiceResults template)
        {
            int faceCount = template.SideMax - template.SideMin + 1;
            float[] perSide = new float[faceCount];
            perSide[faceCount - 1] = count;
            return new DiceResults(perSide, template.SideMin);
        }
    }

    
}