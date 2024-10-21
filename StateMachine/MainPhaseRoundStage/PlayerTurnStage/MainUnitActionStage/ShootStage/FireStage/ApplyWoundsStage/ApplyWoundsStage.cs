

namespace FDG.Stages
{

    public class ApplyWoundsStage : CombatStage<ApplyWoundsResults, ApplyWoundsStage, ICombatMetadata>
    {
        public ApplyWoundsStage(StateMachine stateMachine, ISingleAttackContext<ICombatMetadata> context, StateBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<ApplyWoundsResults> onFinished)
        {
            AssignWoundsResults assignWoundsResults = QueryForResultOrThrowException<AssignWoundsResults>(metaData);

            int modelsKilled = 0;

            foreach(KeyValuePair<IModel, float> kvp in assignWoundsResults.PendingWounds)
            {
                kvp.Key.DealWounds(kvp.Value);

                if(kvp.Key.GetIsDead())
                {
                    modelsKilled++;
                }
            }

            if(metaData.DefendingUnit.GetIsAlive())
            {
                Context.Log($"Applying wounds killed {modelsKilled} models.");
            }
            else
            {
                Context.Log($"Applying wounds killed {modelsKilled} models, killing the unit.");
            }

            onFinished(new ApplyWoundsResults(modelsKilled));
        }
    }
}