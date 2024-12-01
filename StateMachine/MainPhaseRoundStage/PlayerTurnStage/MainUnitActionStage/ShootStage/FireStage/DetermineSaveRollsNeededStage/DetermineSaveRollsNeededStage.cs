
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class DetermineSaveRollsNeededStage<TMetadata> 
        : CombatStage<DetermineSaveRollNeededResults, DetermineSaveRollsNeededStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineSaveRollsNeededStage(IGameContext gameContext, IStateMachineLayer<ISingleAttackContext<TMetadata>> parent) 
            : base(gameContext, parent)
        {
        }

        protected override void RunStage(ICombatMetadata metaData, Action<DetermineSaveRollNeededResults> onFinished)
        {
            List<PendingSaveRolls> pendingSaveRollsList = new List<PendingSaveRolls>();

            int baseDefense = metaData.DefendingUnit.Defense;
            int ap = metaData.WeaponType.ArmorPenetration; //Shorthand.
            int baseDefenseWithAP = baseDefense + ap;

            metaData.TextOutput.Log($"Base roll to save is {baseDefense}, minus {ap}, is {baseDefenseWithAP} (not yet clamped). ");

            RollToHitResults rollToHitResults = QueryForResultOrThrowException<RollToHitResults>(metaData);

            foreach (SuccessfulHitInfo hits in rollToHitResults.SuccessfulHitList)
            {
                //TODO: Provide a way for effects on the hits to affect the wound rolls.
                PendingSaveRolls pendingSaveRolls = new PendingSaveRolls(hits.Rolls, baseDefenseWithAP);


                pendingSaveRollsList.Add(pendingSaveRolls);
            }

            onFinished(new DetermineSaveRollNeededResults(pendingSaveRollsList));
        }
    }
}