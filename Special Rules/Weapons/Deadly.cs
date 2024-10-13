using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Deadly : SpecialRule_Weapon, ICombatEffect<ApplyWoundsResults>
    {
        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<ApplyWoundsResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetaData metadata, ApplyWoundsResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}