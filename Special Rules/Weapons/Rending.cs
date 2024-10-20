
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    [System.Serializable]
    public class Rending : SpecialRule_Weapon, ICombatEffect<DetermineSaveRollNeededResults>, ICombatEffect<RollToSaveResults>
    {
        private const int AP_VALUE_ON_REND_HITS = 4;

        public void OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<DetermineSaveRollNeededResults> sink)
        {

        }

        public void OnPostExecute(ICombatMetadata metadata, DetermineSaveRollNeededResults result)
        {
            foreach (PendingSaveRolls pendingSave in new List<PendingSaveRolls>(result.PendingSaveRollsList))
            {
                IDiceResults rendRolls = pendingSave.HitRolls.SubsetAt(6);
                IDiceResults nonRendRolls = pendingSave.HitRolls.SubsetBelow(6);

                result.PendingSaveRollsList.Remove(pendingSave);

                //Recalculate to use AP4 instead of weapon's AP.
                int oldAP = metadata.WeaponType.ArmorPenetration;
                int apChange = AP_VALUE_ON_REND_HITS - oldAP;
                int newAP = pendingSave.SaveNeeded + apChange;

                metadata.TextOutput.Log($"{nameof(Rending)} converted {rendRolls.TotalRolls} hits from AP({oldAP}) to AP({AP_VALUE_ON_REND_HITS}).");

                result.PendingSaveRollsList.Add(new PendingSaveRolls(rendRolls, newAP));
                result.PendingSaveRollsList.Add(new PendingSaveRolls(nonRendRolls, pendingSave.SaveNeeded));
            }
        }


        //In the Roll to Save results, we remove the Regeneration effect.
        void ICombatEffect<RollToSaveResults>.OnPreExecute(ICombatMetadata metadata, ICombatEffectsSink<RollToSaveResults> sink)
        {
            List<ICombatEffect<RollToSaveResults>> allSinkEffects = sink.OnExecuteEffectsList; //Shorthand.

            List<Regeneration> allRegenEffects =
                new List<Regeneration>(allSinkEffects.OfType<Regeneration>().ToList());

            if (allRegenEffects.Count > 0)
            {
                foreach (Regeneration regenEffect in allRegenEffects)
                {
                    allSinkEffects.Remove(regenEffect);
                }
                metadata.TextOutput.Log($"Rending removed {nameof(Regeneration)} effect.");
            }
        }

        void ICombatEffect<RollToSaveResults>.OnPostExecute(ICombatMetadata metadata, RollToSaveResults result)
        {

        }
    }
}