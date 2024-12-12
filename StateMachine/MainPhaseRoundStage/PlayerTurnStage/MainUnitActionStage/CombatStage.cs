using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public abstract class CombatStage<TResult, TSelf, TMetadata> : StageBase<TMetadata>, ICombatEffectsSink<TResult>
        where TSelf : CombatStage<TResult, TSelf, TMetadata>
        where TMetadata : ICombatMetadata
    {
        //public string FinishedTransitionName;

        public StageBinding NextStage;

        private bool _hasBoundNextStage = false;

        //Not sure if interface needed.
        #region ICombatEffectsSink 
        public List<ICombatEffect<TResult>> OnExecuteEffectsList => _effects;

        #endregion

        private List<ICombatEffect<TResult>> _effects = new List<ICombatEffect<TResult>>();
        protected CombatStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) : base(gameContext, parent)
        {
            NextStage = new StageBinding(this);
        }

        public CombatStage<TNextStageResult, TOtherSelf, TMetadata> BindNextStage<TNextStageResult, TOtherSelf, TMetadata>(CombatStage<TNextStageResult, TOtherSelf, TMetadata> nextStage)
            where TOtherSelf : CombatStage<TNextStageResult, TOtherSelf, TMetadata>
            where TMetadata : ICombatMetadata
        {
            NextStage.Bind(nextStage.Name);

            _hasBoundNextStage = true;

            return nextStage; //For fluid syntax.
        }

        public void BindToEvent(string eventName) //For transitions.
        {
            NextStage.Bind(eventName);

            _hasBoundNextStage = true;
        }

        public override void Enter(TMetadata context)
        {
            foreach (ISpecialRule_Combat rule in context.AllSpecialRules)
            {
                foreach (ICombatEffect<TResult> effect in rule.GetEffects<TResult>())
                {
                    _effects.Add(effect);
                }
            }

            Execute(context);
        }

        public sealed override void Exit()
        {
            base.Exit();

            _effects.Clear();
        }

        private void Execute(TMetadata context)
        {
            if (context.QueryForResult(out TResult _) == true)
            {
                throw new Exception($"Ran combat stage of type {typeof(TResult)} when a result was already present.");
            }

            //Copy the list so that the pre-execute effects can modify it safely.
            List<ICombatEffect<TResult>> effectsCopy = new List<ICombatEffect<TResult>>(_effects);

            foreach (ICombatEffect<TResult> effect in effectsCopy)
            {
                effect.OnPreExecute(context, this);
            }

            RunStage(context, (result) => RunPostExecuteEffects(context, result));
        }

        private void RunPostExecuteEffects(TMetadata context, TResult result)
        {
            //For post-execute effects, use the original, as it may have been purposefully modified in pre-execute.
            foreach (ICombatEffect<TResult> effect in _effects)
            {
                effect.OnPostExecute(context, result);
            }

            context.AddResult(result);

            Finish(context);
        }

        private void Finish(TMetadata context)
        {
            NextStage.Activate(context);
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