using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #034 — resolves ONE target's worth of a damage spell's hits through the shared
    /// save→wound→assign→apply pipeline, with a fresh <see cref="CombatMetadata"/> built per entry from the
    /// next target in the <see cref="SpellDamageRunContext"/>. The spell-damage analog of
    /// <see cref="FireStage"/>: its parent (<see cref="CastSpellStage"/>) loops it once per chosen target,
    /// and each entry's <see cref="GetNewChildContext"/> pops the next target and rolls its hits — so a
    /// multi-unit damage spell hits every selected unit (each with its own AP/Blast/save resolution), not
    /// just the first. The hits run as real dice and go through the hit-complete fold (Blast multiply, on-6
    /// extra hits, Rending AP) before the save flow, exactly as a fired weapon's do.
    /// </summary>
    public class ResolveSpellDamageStage : ParentStage<SpellDamageRunContext, ICombatMetadata>
    {
        public StageBinding OnFinished;

        public ResolveSpellDamageStage(IGameContext gameContext, IStateMachineLayer<SpellDamageRunContext> parent)
            : base(gameContext, parent)
        {
        }

        protected override ICombatMetadata GetNewChildContext(SpellDamageRunContext run)
        {
            DataBinding<UnitData> target = run.ConsumeNextTarget();

            // Roll THIS target's hits and run the hit-complete fold before the save pipeline. Done per target
            // because the Blast cap depends on the target's living-model count.
            (List<SuccessfulHitInfo> hitGroups, int saveModifier, int apReduction) =
                ResolveSpellHits(run.Caster.GetValue(), target.GetValue(), run.BaseHits, run.Weapon);

            CombatMetadata metadata = new CombatMetadata(GameContext, run.Caster, target,
                run.Weapon, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(hitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = saveModifier;
            hitResults.ArmorPenetrationReduction = apReduction;
            metadata.AddResult(hitResults);
            // No cover check runs for a synthetic spell hit; seed a zero bonus so the save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            // #034 single-model targeting: confine all wounds to the one chosen model (single-model spells
            // have a single target, so the pre-picked model belongs to it). Same path Takedown uses — the
            // spell's name (the synthetic weapon is named after it) labels the wound log, not "Takedown".
            if (run.IndividualModel != null)
            {
                metadata.AddResult(new IndividualTargetResult(run.IndividualModel, run.Weapon.Name));
            }

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedEvent);

            return dictionary;
        }

        // Rolls the spell's hits as real dice and runs the hit-complete fold (the same machinery
        // RollToHitStage uses): Blast multiplies (capped at the target's living-model count), "on an
        // unmodified 6" rules add hits, and Rending/Crack promote AP on the natural 6s. The dice faces
        // don't gate the hits — every die is an automatic hit — they only feed the on-6 rules, so the
        // rolled histogram itself is the successful-hit batch the per-hit AP split partitions.
        private (List<SuccessfulHitInfo> hitGroups, int saveModifier, int apReduction) ResolveSpellHits(
            IUnit caster, IUnit target, int baseHits, Weapon spellWeapon)
        {
            IDiceResults rolled = GameContext.DiceRoller.Roll(baseHits);
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(caster, target, out _, out _,
                includeVertical: false);

            // The DEFENDER participates too (Subject seat), mirroring RollToHitStage: Fortified's AP
            // reduction fires against spell damage like any other hit. IsSpell lets rules whose corpus
            // text excludes spells (Shielded) gate themselves out via Condition.IsNotSpell.
            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(caster, target, rolled, distance, IsMelee: false,
                    IsCharging: false, IsSpell: true),
                (caster, ERuleSeat.Actor, spellWeapon, (IReadOnlyList<IModel>?)null, EModelRuleScope.AnyOwner),
                (target, ERuleSeat.Subject, (IWeapon?)null, (IReadOnlyList<IModel>?)null,
                    EModelRuleScope.AnyOwner));

            // Split the auto-hitting rolled dice so a natural 6 carries Rending/Crack AP per-hit while the
            // rest save at base AP. No per-hit rule → a single base-AP group holding every rolled hit.
            List<SuccessfulHitInfo> groups = PerHitApSplitter.Split(rolled, ops);

            // Surge-style extra hits are base AP (a generated hit is not itself a natural 6).
            HitInjectionSink injection = new HitInjectionSink();
            injection.ApplyFrom(ops);
            if (injection.TotalExtraHits > 0f)
            {
                groups.Add(new SuccessfulHitInfo(SyntheticHits(injection.TotalExtraHits)));
            }

            // Blast multiplies the POST-injection total, capped at the target's living-model count; the
            // added hits are base AP. Mirrors RollToHitStage — the original groups (including the AP group)
            // are untouched and only the capped extra is appended.
            HitMultiplierSink multiplier = new HitMultiplierSink();
            multiplier.ApplyFrom(ops);
            if (multiplier.NetMultiplier > 1)
            {
                float currentHits = TotalHits(groups);
                float cappedHits = System.Math.Min(currentHits * multiplier.NetMultiplier, CountLivingModels(target));
                float extraHits = cappedHits - currentHits;
                if (extraHits > 0f)
                {
                    groups.Add(new SuccessfulHitInfo(SyntheticHits(extraHits)));
                }
            }

            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(ops);

            // Fortified (defender) reduces the incoming spell AP, floored at the save stage — the same
            // fold RollToHitStage does for weapon attacks.
            int apReduction = ops.OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            return (groups, saveModifiers.Net(ERollKind.Save), apReduction);
        }

        private static float TotalHits(List<SuccessfulHitInfo> groups)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo group in groups)
            {
                total += group.HitCount;
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

        // Bridges a scalar hit count into the IDiceResults the save flow consumes (mirrors
        // StrafingStage.SyntheticHits / ResolveImpactHitsStage). The face is cosmetic — saves count by total.
        private static IDiceResults SyntheticHits(float count)
        {
            float[] perSide = new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT];
            perSide[perSide.Length - 1] = count;
            return new DiceResults(perSide);
        }
    }
}
