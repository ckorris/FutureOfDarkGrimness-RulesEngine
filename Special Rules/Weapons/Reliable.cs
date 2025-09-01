using FDG.Utilities;
using System.Collections.Generic;

namespace FDG
{
    [System.Serializable]
    public class Reliable : SpecialRule_Weapon, ICombatEffect<DetermineHitRollNeededResults>
    {
        private const int BASE_ROLL_WITH_RELIABLE = 2;

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<DetermineHitRollNeededResults> sink) { }

        public void OnPostExecute(ICombatMetadata metadata, DetermineHitRollNeededResults result)
        {
            int unitQuality = metadata.AttackingUnit.Quality();
            int adjustment = BASE_ROLL_WITH_RELIABLE - unitQuality;

            metadata.TextOutput().Log($"{nameof(Reliable)} adjusted attacker's effective quality from {unitQuality} to {BASE_ROLL_WITH_RELIABLE}.");

            result.HitRollNeeded += adjustment;
        }
    }
}