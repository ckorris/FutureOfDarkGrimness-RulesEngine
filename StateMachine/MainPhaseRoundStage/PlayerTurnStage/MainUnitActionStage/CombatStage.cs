using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public abstract class CombatStage<TResult, TSelf, TMetadata> : StageBase<ISingleAttackContext>, ICombatEffectsSink<TResult>
        where TSelf : CombatStage<TResult, TSelf, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public string FinishedTransitionName;

        private readonly StateMachine _stateMachine;
        private readonly ISingleAttackContext<TMetadata> _context;

        private bool _hasBoundNextStage = false;

        public CombatStage(StateMachine stateMachine, ISingleAttackContext<TMetadata> context, StageBase parentState = null) 
            : base(stateMachine, context, parentState)
        {
            _stateMachine = stateMachine;
            _context = context;
        }

        //Not sure if interface needed.
        #region ICombatEffectsSink 
        public List<ICombatEffect<TResult>> OnExecuteEffectsList => _effects;

        #endregion

        private List<ICombatEffect<TResult>> _effects = new List<ICombatEffect<TResult>>();

        public CombatStage<TNextStageResult, TOtherSelf, TMetadata> BindNextStage<TNextStageResult, TOtherSelf, TMetadata>(CombatStage<TNextStageResult, TOtherSelf, TMetadata> nextStage)
            where TOtherSelf : CombatStage<TNextStageResult, TOtherSelf, TMetadata>
            where TMetadata : ICombatMetadata
        {
            if(_hasBoundNextStage)
            {
                throw new InvalidOperationException($"Tried to bind next stage of {GetType()} to {typeof(TNextStageResult)}, "
                    + $"but it had already been bound. Existing transition name: {FinishedTransitionName}");
            }

            FinishedTransitionName = GetTransitionName(nextStage);

            Bind(FinishedTransitionName, nextStage);

            _hasBoundNextStage = true;

            return nextStage; //For fluid syntax.
        }

        public void BindNextStage(StageBase nextStage)
        {
            if (_hasBoundNextStage)
            {
                throw new InvalidOperationException($"Tried to bind next stage of {GetType()} to {nextStage.GetType()}, "
                    + $"but it had already been bound. Existing transition name: {FinishedTransitionName}");
            }

            FinishedTransitionName = GetTransitionName(nextStage);
            Bind(FinishedTransitionName, nextStage);
            _hasBoundNextStage = true;
        }

        private string GetTransitionName(StageBase nextStage)
        {
            return $"{GetType()}_TO_{nextStage.GetType()}";
        }

        public sealed override void Enter()
        {
            base.Enter();

            foreach (ISpecialRule_Combat rule in _context.AllSpecialRules)
            {
                foreach (ICombatEffect<TResult> effect in rule.GetEffects<TResult>())
                {
                    _effects.Add(effect);
                }
            }

            Execute();
        }

        public sealed override void Exit()
        {
            base.Exit();

            _effects.Clear();
        }

        private void Execute()
        {
            TMetadata metaData = _context.CombatMetaData; //Shorthand.

            if (metaData.QueryForResult(out TResult _) == true)
            {
                throw new Exception($"Ran combat stage of type {typeof(TResult)} when a result was already present.");
            }

            //Copy the list so that the pre-execute effects can modify it safely.
            List<ICombatEffect<TResult>> effectsCopy = new List<ICombatEffect<TResult>>(_effects);

            foreach (ICombatEffect<TResult> effect in effectsCopy)
            {
                effect.OnPreExecute(metaData, this);
            }

            RunStage(metaData, RunPostExecuteEffects);
        }

        private void RunPostExecuteEffects(TResult result)
        {
            ICombatMetadata metaData = _context.CombatMetaData; //Shorthand.

            //For post-execute effects, use the original, as it may have been purposefully modified in pre-execute.
            foreach (ICombatEffect<TResult> effect in _effects)
            {
                effect.OnPostExecute(metaData, result);
            }

            metaData.AddResult(result);

            Finish();
        }

        private void Finish()
        {
            SignalEvent(FinishedTransitionName);
        }

        protected TQueryResult QueryForResultOrThrowException<TQueryResult>(ICombatMetadata metaData)
        {
            //TODO: Add a check for this ahead of time somehow.
            bool found = metaData.QueryForResult(out TQueryResult result);

            if (found == false)
            {
                throw new Exception($"Combat stage of type {GetType()} required existing results of type {typeof(TQueryResult)}, " +
                    "but it didn't exist.");
            }

            return result;
        }

        protected abstract void RunStage(ICombatMetadata metaData, Action<TResult> onFinished);
    }
}