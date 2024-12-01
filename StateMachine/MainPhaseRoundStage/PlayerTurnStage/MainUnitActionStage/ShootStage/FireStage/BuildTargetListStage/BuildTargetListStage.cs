
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class BuildTargetListStage : CombatStage<BuildTargetListResults, BuildTargetListStage, ICombatMetadata>
    {
        public BuildTargetListStage(IGameContext gameContext, IStateMachineLayer<ISingleAttackContext<ICombatMetadata>> parent) : base(gameContext, parent)
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