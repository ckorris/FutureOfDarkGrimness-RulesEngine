
namespace FDG
{
    public interface ICombatEffectsSink<TResult>
    {
        /// <summary>
        /// Reference to the sink's list of all effects that have been registered to the sink. 
        /// This should not be a copy of the list, so that effects can remove effects or change the order
        /// so that post-execution effects resolve in a particular way.
        /// </summary>
        public List<ICombatEffect<TResult>> OnExecuteEffectsList { get; }
    }
}