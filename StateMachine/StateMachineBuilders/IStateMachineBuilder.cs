
namespace FDG.Stages.Builders
{
    public interface IStateMachineBuilder<TTopLevel>
    {
        Dictionary<string, StageBase<TTopLevel>> BuildStateMachine(StateMachine<TTopLevel> stateMachine, IGameContext gameContext,
            out StageBase<TTopLevel> startingStage);
    }
}