namespace FDG
{
    public interface ICombatEffect<TResult>
    {
        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<TResult> sink); //TODO: Need to revisit how it gets info.

        public void OnPostExecute(ICombatMetaData metadata, TResult result);
    }
}