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
            // because the Blast cap depends on the target's living-model count. The fold itself is shared
            // with the ability / Strafing DealHits paths (#164).
            SyntheticHitResolution.Result hits = SyntheticHitResolution.Resolve(
                GameContext, run.Caster.GetValue(), target.GetValue(), run.BaseHits, run.Weapon, isSpell: true);

            CombatMetadata metadata = new CombatMetadata(GameContext, run.Caster, target,
                run.Weapon, weaponCount: 1, isMelee: false, isSpell: true);

            RollToHitResults hitResults = new RollToHitResults(hits.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = hits.SaveModifier;
            hitResults.ArmorPenetrationReduction = hits.ArmorPenetrationReduction;
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
    }
}
