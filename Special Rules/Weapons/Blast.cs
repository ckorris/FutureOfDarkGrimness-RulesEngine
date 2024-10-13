
namespace FDG
{
    [System.Serializable]
    public class Blast : SpecialRule_Weapon, ICombatEffect<CoverCheckResults>, ICombatEffect<AssignWoundsResults>
    {
        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<CoverCheckResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPreExecute(ICombatMetaData metadata, ICombatEffectsSink<AssignWoundsResults> sink)
        {
            throw new System.NotImplementedException();
        }

        public void OnPostExecute(ICombatMetaData metadata, CoverCheckResults result)
        {
            throw new System.NotImplementedException();
        }
        public void OnPostExecute(ICombatMetaData metadata, AssignWoundsResults result)
        {
            throw new System.NotImplementedException();
        }
    }
}