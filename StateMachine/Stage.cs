
using System;



namespace FDG.Stages
{
    public interface IStage
    {
        /// <summary>
        /// Called when the state is entered.
        /// </summary>
        void Enter();

        /// <summary>
        /// Called when the state is exited.
        /// </summary>
        void Exit();
    }

    public interface IContextAware
    {
        void SetContext(object context);
    }

    public abstract class StageBase<TContext> : StageBase, IContextAware
        where TContext : IGameContextAccessor
    {
        /// <summary>
        /// The context associated with this state.
        /// </summary>
        public TContext Context { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StageBase{TContext}"/> class.
        /// </summary>
        /// <param name="stateMachine">The state machine managing this state.</param>
        /// <param name="context">The context associated with this state.</param>
        /// <param name="parentState">The parent state, if any.</param>
        protected StageBase(StateMachine stateMachine, TContext context, StageBase parentState = null)
            : base(stateMachine, parentState)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void SetContext(object context)
        {
            if(context is TContext typedContext)
            {
                Context = typedContext;
            }
            else
            {
                throw new ArgumentException($"Called {nameof(SetContext)} on state {GetType()} with context that wasn't " +
                    $"expected type of {typeof(TContext)}. It was {context.GetType()} instead.");
            }
        }
    }

    public abstract class StageBase : IStage
    {
        protected StateMachine StateMachine { get; }
        protected StageBase ParentState { get; }

        protected StageBase(StateMachine stateMachine, StageBase parentState = null)
        {
            StateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            ParentState = parentState;
        }

        public void Bind(string eventName, StageBase targetState)
        {
            StateMachine.AddTransition(this, eventName, targetState);
        }

        public virtual void Enter() { }
        public virtual void Exit() { }

        public void SignalEvent(string eventName, object eventData = null)
        {
            StateMachine.ProcessEvent(this, eventName, eventData);
        }
    }

}