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
            (float finalHits, int saveModifier) =
                ResolveSpellHits(run.Caster.GetValue(), target.GetValue(), run.BaseHits, run.Weapon);

            CombatMetadata metadata = new CombatMetadata(GameContext, run.Caster, target,
                run.Weapon, weaponCount: 1, isMelee: false);

            RollToHitResults hitResults = new RollToHitResults(
                new List<SuccessfulHitInfo>() { new SuccessfulHitInfo(SyntheticHits(finalHits)) },
                new List<FailedHitInfo>());
            hitResults.SaveModifier = saveModifier;
            metadata.AddResult(hitResults);
            // No cover check runs for a synthetic spell hit; seed a zero bonus so the save stage won't throw.
            metadata.AddResult(new CoverCheckResults(0));

            // #034 single-model targeting: confine all wounds to the one chosen model (single-model spells
            // have a single target, so the pre-picked model belongs to it). Same path Takedown uses.
            if (run.IndividualModel != null)
            {
                metadata.AddResult(new IndividualTargetResult(run.IndividualModel));
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
        // unmodified 6" rules add hits, and Rending promotes AP into a carried save modifier. The dice
        // faces don't gate the hits — every die is an automatic hit — they only feed the on-6 rules.
        private (float hits, int saveModifier) ResolveSpellHits(IUnit caster, IUnit target, int baseHits, Weapon spellWeapon)
        {
            IDiceResults rolled = GameContext.DiceRoller.Roll(baseHits);
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(caster, target, out _, out _,
                includeVertical: false);

            IReadOnlyList<RuleOperation> ops = GameContext.RuleEvaluator.EvaluateAll(
                new HitRollCompleteContext(caster, target, rolled, distance, false, false),
                (caster, ERuleSeat.Actor, spellWeapon, (IReadOnlyList<IModel>?)null, EModelRuleScope.AnyOwner));

            HitInjectionSink injection = new HitInjectionSink();
            injection.ApplyFrom(ops);
            float hits = rolled.TotalRolls + injection.TotalExtraHits;

            HitMultiplierSink multiplier = new HitMultiplierSink();
            multiplier.ApplyFrom(ops);
            if (multiplier.NetMultiplier > 1)
            {
                hits = System.Math.Min(hits * multiplier.NetMultiplier, CountLivingModels(target));
            }

            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(ops);
            return (hits, saveModifiers.Net(ERollKind.Save));
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
