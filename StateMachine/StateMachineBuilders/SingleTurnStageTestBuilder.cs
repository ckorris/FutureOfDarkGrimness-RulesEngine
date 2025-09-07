using FDG.Stages;
using FDG.Stages.Builders;


namespace FDG.StateMachine.StateMachineBuilders
{
    /// <summary>
    /// Builder that builds a state machine that just executes a single unit turn. You'll have to create a context 
    /// when starting it that has some info already set.
    /// </summary>
    internal class SingleTurnStageTestBuilder : IStateMachineBuilder<ISingleRoundContext>
    {
        
        public Dictionary<string, StageBase<ISingleRoundContext>> BuildStateMachine(StateMachine<ISingleRoundContext> stateMachine, 
            IGameContext gameContext, out StageBase<ISingleRoundContext> startingStage)
        {
            SingleTurnStage singleTurnStage = new SingleTurnStage(gameContext, stateMachine);
            EmptyEndStage<ISingleRoundContext> emptyEndStage = new EmptyEndStage<ISingleRoundContext>(gameContext, null);

            singleTurnStage.OnTurnFinished.Bind(emptyEndStage);

            startingStage = singleTurnStage;

            Dictionary<string, StageBase<ISingleRoundContext>> bindings = new Dictionary<string, StageBase<ISingleRoundContext>>()
            {
                {singleTurnStage.Name, singleTurnStage},
                {emptyEndStage.Name, emptyEndStage},
            };

            return bindings;
        }
    }
}
