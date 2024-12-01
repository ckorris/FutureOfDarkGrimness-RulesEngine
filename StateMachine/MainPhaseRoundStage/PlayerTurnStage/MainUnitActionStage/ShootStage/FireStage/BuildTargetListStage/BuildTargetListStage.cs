
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class BuildTargetListStage : CombatStage<BuildTargetListResults, BuildTargetListStage, ICombatMetadata>
    {
        public BuildTargetListStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StageBase parentState = null)
            : base(stateMachine, context, parentState)
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