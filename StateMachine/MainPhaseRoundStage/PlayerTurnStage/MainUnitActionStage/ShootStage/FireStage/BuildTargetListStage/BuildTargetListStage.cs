
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class BuildTargetListStage<TMetadata> : CombatStage<BuildTargetListResults, BuildTargetListStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public BuildTargetListStage(IGameContext gameContext, IStateMachineLayer<ISingleAttackContext<TMetadata>> parent)
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<BuildTargetListResults> onFinished)
        {
            List<IModel> targets = new List<IModel>();

            targets.AddRange(metaData.DefendingUnit.Models);

            string pluralizedModelWord = (targets.Count == 1) ? "model" : "models";
            metaData.TextOutput.Log($"Created ordered target list of {targets.Count} {pluralizedModelWord}.");

            BuildTargetListResults results = new BuildTargetListResults(targets);

            onFinished(results);
        }
    }

    
}