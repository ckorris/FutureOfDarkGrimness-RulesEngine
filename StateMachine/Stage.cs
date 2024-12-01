using System;



namespace FDG.Stages
{
    public abstract partial class StageBase<TContext>
    {
        public string Name => this.Name();

        public static string NameOf<T>() where T : StageBase<TContext>
        {
            return typeof(T).Name;
        }

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

        public void Bind(string eventName, StageBase<TContext> targetStage)
        {

        }

        public void SignalEvent(string eventName, TContext context)
        {

        }

        public abstract void Enter(TContext context);

        public virtual void Exit() { } //Optional.
    }

    public static class StageBaseExtensions
    {
        public static string Name<TContext>(this StageBase<TContext> stage)
        {
            return stage.GetType().Name;
        }
    }

    /*
    public abstract class StageBase
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
    */
}