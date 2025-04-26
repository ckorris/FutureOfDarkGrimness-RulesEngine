using FDG.Stages.Builders;
using FDG.StateMachine;

namespace FDG.Stages
{
    public class StateMachine<TTopLevel> : IStateMachine, IStateMachineLayer<TTopLevel>
    {
        public IReadOnlyList<IStage> ActiveStages => _activeStages;

        private List<IStage> _activeStages = new List<IStage>();

        private Dictionary<string, StageBase<TTopLevel>> _transitions;

        private StageBase<TTopLevel> _startingStage;

        public StateMachine(IStateMachineBuilder<TTopLevel> builder, IGameContext gameContext)
        {
            _transitions = builder.BuildStateMachine(this, gameContext, out _startingStage);
        }

        public async Task Enter(TTopLevel topLevelContext)
        {
            await _startingStage.Enter(topLevelContext);
        }

        public async Task ExecuteTransition(string eventName, StageBase<TTopLevel> leavingChild, TTopLevel childContext)
        {
            if (_transitions.TryGetValue(eventName, out StageBase<TTopLevel> enteringChild) == false)
            {
                throw new KeyNotFoundException();
            }

            leavingChild.Exit();
            NotifyChildExited(leavingChild);

            await enteringChild.Enter(childContext);
            NotifyChildEntered(enteringChild);
        }

        public void NotifyChildEntered(IStage enteredStage)
        {
            _activeStages.Add(enteredStage);
        }

        public void NotifyChildExited(IStage enteredStage)
        {
            _activeStages.Remove(enteredStage);
        }
    }
}