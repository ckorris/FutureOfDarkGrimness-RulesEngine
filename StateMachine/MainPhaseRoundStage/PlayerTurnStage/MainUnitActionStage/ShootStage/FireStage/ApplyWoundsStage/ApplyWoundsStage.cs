using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class ApplyWoundsStage<TMetadata> : CombatStage<ApplyWoundsResults, ApplyWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public ApplyWoundsStage(IGameContext gameContext, IStateMachineLayer<ISingleAttackContext<TMetadata>> parent) 
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<ApplyWoundsResults> onFinished)
        {
            AssignWoundsResults assignWoundsResults = QueryForResultOrThrowException<AssignWoundsResults>(metaData);

            float totalWoundsApplied = 0;
            int modelsKilled = 0;

            foreach(KeyValuePair<IModel, float> kvp in assignWoundsResults.PendingWounds)
            {
                float woundsToDeal = kvp.Value;
                float modelRemainingWounds = kvp.Key.TotalWounds - kvp.Key.WoundsDealt;

                if (woundsToDeal > modelRemainingWounds)
                {
                    throw new Exception($"Tried to deal {woundsToDeal} to a model with only {modelRemainingWounds} left.");
                }

                kvp.Key.DealWounds(kvp.Value);
                totalWoundsApplied += kvp.Value;

                if(kvp.Key.GetIsDead())
                {
                    modelsKilled++;
                }
            }

            if(metaData.DefendingUnit.GetIsAlive())
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models.");
            }
            else
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models, killing the unit.");
            }

            onFinished(new ApplyWoundsResults(modelsKilled));
        }
    }
}