
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class BuildTargetListStage : CombatStage<BuildTargetListResults, BuildTargetListStage>
    {
        public BuildTargetListStage(StateMachine stateMachine, ISingleAttackContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {

        }

        protected override void RunStage(ICombatMetaData metaData, Action<BuildTargetListResults> onFinished)
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