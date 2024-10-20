
namespace FDG
{
    [System.Serializable]
    public class Blast : SpecialRule_Weapon, ICombatEffect<CoverCheckResults>, ICombatEffect<AssignWoundsResults>
    {
        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<CoverCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<AssignWoundsResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetadata metadata, CoverCheckResults result)
        {
            throw new System.NotImplementedException();
        }
        public void OnPostExecute(ICombatMetadata metadata, AssignWoundsResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}