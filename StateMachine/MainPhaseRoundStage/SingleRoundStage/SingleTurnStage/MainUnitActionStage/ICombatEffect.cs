namespace FDG
{
    public interface ICombatEffect<TResult>
    {
        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<TResult> sink); //TODO: Need to revisit how it gets info.

        public void OnPostExecute(ICombatMetadata metadata, TResult result);
    }
}