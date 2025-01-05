using FDG.StateMachine;
using System;



namespace FDG.Stages
{
    public abstract partial class StageBase<TContext> : IStage
    {
        public string Name => GetType().Name;

        /// <summary>
        /// The context associated with this state.
        /// </summary>
        public IGameContext GameContext { get; private set; }

        public IStateMachineLayer<TContext> Parent { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StageBase{TContext}"/> class.
        /// </summary>
        /// <param name="stateMachine">The state machine managing this state.</param>
        /// <param name="gameContext">The context associated with this state.</param>
        /// <param name="parent">The parent state, if any.</param>
        protected StageBase(IGameContext gameContext, IStateMachineLayer<TContext> parent)
        {
            GameContext = gameContext ?? throw new ArgumentNullException(nameof(gameContext));
            Parent = parent;
        }

        public void SignalEvent(string eventName, TContext context)
        {
            Parent.ExecuteTransition(eventName, this, context);
        }

        public abstract void Enter(TContext context);

        public virtual void Exit() { } //Optional.
    }
}