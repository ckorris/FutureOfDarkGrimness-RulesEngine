
using FDG.Data;
using FDG.Utilities;
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class BuildTargetListStage<TMetadata> : CombatStage<BuildTargetListResults, BuildTargetListStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public BuildTargetListStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<BuildTargetListResults> onFinished)
        {
            List<DataBinding<ModelData>> targets = new List<DataBinding<ModelData>>();

            targets.AddRange(metaData.DefendingUnit.ModelBindings());

            string pluralizedModelWord = (targets.Count == 1) ? "model" : "models";
            GameContext.Log($"Created ordered target list of {targets.Count} {pluralizedModelWord}.");

            BuildTargetListResults results = new BuildTargetListResults(targets);

            onFinished(results);
        }
    }

    
}