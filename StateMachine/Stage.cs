using FDG.StateMachine;
using System;



namespace FDG.Stages
{
    public abstract partial class StageBase<TContext> : IStage
    {
        // The transition key for this stage within its parent. Defaults to the type name (one instance
        // per stage type per parent — the common case). Virtual so a stage used as MULTIPLE sibling
        // instances under one parent can give each instance a distinct name and avoid a transition-key
        // collision.
        public virtual string Name => GetType().Name;

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

        public Task SignalEvent(string eventName, TContext context)
        {
            return Parent.ExecuteTransition(eventName, this, context);
        }

        public abstract Task Enter(TContext context);

        public virtual void Exit() { } //Optional.
    }
}