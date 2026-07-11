
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
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

            // #006: a joined hero saves at the unit's Defense until it is the sole survivor, then its own.
            int baseDefense = HeroStatRules.GetSaveDefense(metaData.DefendingUnit.GetValue());
            CoverCheckResults coverResults = QueryForResultOrThrowException<CoverCheckResults>(metaData);

            RollToHitResults rollToHitResults = QueryForResultOrThrowException<RollToHitResults>(metaData);

            // Fortified (defender) reduces the WEAPON's AP, floored at 0 — so it does nothing against an
            // AP(0) hit, unlike a flat +1 save. Rule-granted AP (Rending/Thrust) rides SaveModifier below
            // and is not reduced here.
            int ap = Math.Max(0, metaData.WeaponType.ArmorPenetration - rollToHitResults.ArmorPenetrationReduction);

            // #042 save-modifier rules (Rending) folded their net modifier at hit-roll-complete and
            // carried it here; a negative delta to the save roll raises the threshold. Subtract it,
            // mirroring the hit stage's `HitRollNeeded -= Net(Hit)` convention.
            // #033 granted "+X to defense" buffs on the defender (one-shot / duration) lower the threshold,
            // same sign as cover; one-shot ("next time") grants are consumed by this attack's saves.
            int grantedDefense = GrantedRollModifiers.ConsumeNet(metaData.DefendingUnit.GetValue(), ERollKind.Save);

            int baseDefenseWithAP = baseDefense + ap - coverResults.DefenseRollBonus - rollToHitResults.SaveModifier
                - grantedDefense;

            GameContext.Log($"Base roll to save is {baseDefense}, AP {ap}, cover -{coverResults.DefenseRollBonus}, " +
                $"Rending/save mods {rollToHitResults.SaveModifier}, effective threshold {baseDefenseWithAP} (not yet clamped).");

            foreach (SuccessfulHitInfo hits in rollToHitResults.SuccessfulHitList)
            {
                // #016 Per-hit-group effects: a save modifier tagged on this specific hit group shifts
                // its threshold, stacking on top of the unit-wide modifiers already folded into
                // baseDefenseWithAP (same sign convention — a negative raises the threshold).
                int saveNeeded = baseDefenseWithAP - hits.SaveModifier;
                PendingSaveRolls pendingSaveRolls = new PendingSaveRolls(hits.Rolls, saveNeeded, hits.Source);

                pendingSaveRollsList.Add(pendingSaveRolls);
            }

            await onFinished(new DetermineSaveRollNeededResults(pendingSaveRollsList));
        }
    }
}