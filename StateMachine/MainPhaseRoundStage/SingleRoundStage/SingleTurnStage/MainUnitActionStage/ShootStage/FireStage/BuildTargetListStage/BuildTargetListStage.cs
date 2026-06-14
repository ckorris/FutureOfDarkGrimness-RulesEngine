
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{
    public class BuildTargetListStage<TMetadata> : CombatStage<BuildTargetListResults, BuildTargetListStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public BuildTargetListStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<BuildTargetListResults, Task> onFinished)
        {
            List<DataBinding<ModelData>> targets = new List<DataBinding<ModelData>>();

            targets.AddRange(metaData.DefendingUnit.ModelBindings());

            string pluralizedModelWord = (targets.Count == 1) ? "model" : "models";
            GameContext.Log($"Created ordered target list of {targets.Count} {pluralizedModelWord}.");

            BuildTargetListResults results = new BuildTargetListResults(targets);

            // #042 targets-selected rules (Takedown): the shooter may pick one model in the target unit
            // and resolve the attack against just it ("a unit of [1]"). Fire OnShootTargetsSelected for
            // the attacker; if TargetIndividualModel is queued, ask the attacker to pick a living model
            // and stash it for AssignWoundsStage to confine wound allocation. Shooting only — the hook is
            // a shooting "when" and melee reuses this stage. The hit/save resolution is unchanged; only
            // the wound recipients are restricted (LoS/cover-ignore facet deferred — see Appendix C).
            if (!metaData.IsMelee)
            {
                await MaybePickIndividualTarget(metaData);
            }

            await onFinished(results);
        }

        private async Task MaybePickIndividualTarget(ICombatMetadata metaData)
        {
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();

            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new ShootTargetsSelectedContext(attacker, defender),
                (attacker, ERuleSeat.Actor, metaData.WeaponType));

            if (!operations.OfType<RuleOperation.TargetIndividualModel>().Any())
            {
                return;
            }

            List<DataBinding<ModelData>> livingModels = metaData.DefendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive())
                .ToList();

            if (livingModels.Count == 0) return;

            var validOptions = new List<SelectionRequest<ModelData>.ValidOption>();
            for (int i = 0; i < livingModels.Count; i++)
            {
                validOptions.Add(new SelectionRequest<ModelData>.ValidOption(livingModels[i], $"Model {i + 1}"));
            }

            // The attack has already committed to Takedown — picking the target model is mandatory, no cancel.
            SelectionRequest<ModelData> request = new SelectionRequest<ModelData>(
                metaData.AttackingUnit.PlayerID(), "Takedown: choose the target model",
                validOptions, Array.Empty<SelectionRequest<ModelData>.InvalidOption>(), allowCancel: false);

            DataBinding<ModelData> chosen = await GameContext.PlayerRequester
                .RequestDecision<SelectionRequest<ModelData>, DataBinding<ModelData>>(request);

            metaData.AddResult(new IndividualTargetResult(chosen));
            GameContext.Log($"{attacker.Name}'s Takedown targets a single model in {defender.Name}.");
        }
    }

    /// <summary>
    /// Records that Takedown re-scoped the in-flight shooting attack to a single model. Produced by
    /// <see cref="BuildTargetListStage{TMetadata}"/> and read by AssignWoundsStage, which confines all
    /// wounds to this model (capped, no carry-over).
    /// </summary>
    public readonly record struct IndividualTargetResult(DataBinding<ModelData> Model);
}
