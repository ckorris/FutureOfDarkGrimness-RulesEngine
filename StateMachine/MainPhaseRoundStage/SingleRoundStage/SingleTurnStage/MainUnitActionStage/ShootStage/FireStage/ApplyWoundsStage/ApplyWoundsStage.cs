using FDG.Utilities;

namespace FDG.Stages
{

    public class ApplyWoundsStage<TMetadata> : CombatStage<ApplyWoundsResults, ApplyWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public ApplyWoundsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) 
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<ApplyWoundsResults> onFinished)
        {
            AssignWoundsResults assignWoundsResults = QueryForResultOrThrowException<AssignWoundsResults>(metaData);

            float totalWoundsApplied = 0;
            int modelsKilled = 0;

            foreach(PendingWounds pendingWound in assignWoundsResults.PendingWounds)
            {
                ModelData model = pendingWound.Model; //Shorthand.
                float woundsToDeal = pendingWound.Wounds;

                float modelRemainingWounds = model.TotalWounds - model.WoundsDealt;

                if (woundsToDeal > modelRemainingWounds)
                {
                    throw new Exception($"Tried to deal {woundsToDeal} to a model with only {modelRemainingWounds} left.");
                }

                model.DealWounds(woundsToDeal);
                totalWoundsApplied += woundsToDeal;

                if(model.GetIsDead())
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