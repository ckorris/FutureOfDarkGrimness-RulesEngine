
using FDG.Utilities;
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class DetermineSaveRollsNeededStage<TMetadata> 
        : CombatStage<DetermineSaveRollNeededResults, DetermineSaveRollsNeededStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineSaveRollsNeededStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) 
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<DetermineSaveRollNeededResults, Task> onFinished)
        {
            List<PendingSaveRolls> pendingSaveRollsList = new List<PendingSaveRolls>();

            int baseDefense = metaData.DefendingUnit.Defense();
            int ap = metaData.WeaponType.ArmorPenetration; //Shorthand.
            CoverCheckResults coverResults = QueryForResultOrThrowException<CoverCheckResults>(metaData);

            RollToHitResults rollToHitResults = QueryForResultOrThrowException<RollToHitResults>(metaData);

            // #042 save-modifier rules (Rending) folded their net modifier at hit-roll-complete and
            // carried it here; a negative delta to the save roll raises the threshold. Subtract it,
            // mirroring the hit stage's `HitRollNeeded -= Net(Hit)` convention.
            int baseDefenseWithAP = baseDefense + ap - coverResults.DefenseRollBonus - rollToHitResults.SaveModifier;

            GameContext.Log($"Base roll to save is {baseDefense}, AP {ap}, cover -{coverResults.DefenseRollBonus}, " +
                $"Rending/save mods {rollToHitResults.SaveModifier}, effective threshold {baseDefenseWithAP} (not yet clamped).");

            foreach (SuccessfulHitInfo hits in rollToHitResults.SuccessfulHitList)
            {
                //TODO: Provide a way for effects on the hits to affect the wound rolls.
                PendingSaveRolls pendingSaveRolls = new PendingSaveRolls(hits.Rolls, baseDefenseWithAP);


                pendingSaveRollsList.Add(pendingSaveRolls);
            }

            await onFinished(new DetermineSaveRollNeededResults(pendingSaveRollsList));
        }
    }
}