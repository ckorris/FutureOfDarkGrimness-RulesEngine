using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Deadly : SpecialRule_Weapon, ICombatEffect<ApplyWoundsResults>
    {
        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<ApplyWoundsResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, ApplyWoundsResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}