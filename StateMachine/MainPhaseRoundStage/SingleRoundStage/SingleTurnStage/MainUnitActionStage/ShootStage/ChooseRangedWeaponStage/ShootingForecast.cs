using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Stages
{
    /// <summary>
    /// #325: the pre-roll forecast stamped onto each weapon-x-target row of a
    /// <see cref="ChooseRangedAttackRequest"/> - the effective to-hit and save thresholds the dice
    /// will use, with the same modifier chips the roll beats show, so the targeting UI can put the
    /// numbers in front of the player BEFORE the commit. Computed engine-side because a resolver
    /// cannot: weapon rules never cross the wire (<c>IWeapon.RuleDefinitions</c> is [JsonIgnore]).
    ///
    /// <para>
    /// A read-only arithmetic mirror of DetermineHitRollStage, RollToHitStage's whole-attack save
    /// fold, and DetermineSaveRollsNeededStage - the CombatMath pattern, but keeping the intermediate
    /// numbers instead of collapsing to expected wounds. All rule effects go through
    /// <see cref="RuleEvaluator.EvaluateAllNamed"/> (the read-only twin: no logs, no one-shot-grant
    /// spends) with the stages' own contexts, participants and sinks, and the chips come from the
    /// stages' own compose methods - so the preview and the post-roll beat can never say different
    /// things about the same modifier.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT priced (roll-time facts, surfaced via <c>AttackForecast.Notes</c> where they
    /// exist as tokens): spendable Tag/Spotter markers (claiming is a mid-roll prompt), an unclaimed
    /// Mark on the target (claiming mutates tokens), face-triggered effects (Rending's per-hit AP,
    /// Furious' extra hits - natural 6s), the Unpredictable branch die, and Regenerative Strength's
    /// attack-count prompt. Granted one-shot buffs and persistent markers ARE priced - both are
    /// readable without consuming, and each offer pass recomputes, so a buff spent by an earlier
    /// volley of the same action has already left the forecast.
    /// </para>
    /// </summary>
    public static class ShootingForecast
    {
        /// <summary>
        /// Stamps a forecast onto every row that has at least one shooter. Rows with no models in
        /// range/sight keep a null forecast - there is nothing to price and the UI has nothing to say.
        /// Called once per weapon offer (not from <c>HasAnyFireableTarget</c>, which ChooseActionStage
        /// polls every time the action menu opens and only needs the boolean).
        /// </summary>
        public static void Attach(List<WeaponOption> weaponOptions, DataBinding<UnitData> attackingUnit,
            IGameContext gameContext, bool attackerMoved)
        {
            foreach (WeaponOption option in weaponOptions)
            {
                for (int i = 0; i < option.WeaponTargetStats.Count; i++)
                {
                    WeaponTargetStats stats = option.WeaponTargetStats[i];
                    if (stats.modelsThatCanShoot.Count == 0) continue;

                    // #345: the size of the volley rides along with the thresholds. Stamped here rather than
                    // inside Compute because it is a property of THIS row's shooter set, not of the
                    // attacker-x-target arithmetic Compute mirrors.
                    (int firing, int potential) = ChooseRangedAttackStage.AttackCounts(option, stats);

                    option.WeaponTargetStats[i] = stats with
                    {
                        Forecast = Compute(gameContext, attackingUnit, option.Weapon, stats.TargetUnit,
                            stats.HasCover, option.IgnoresCover, attackerMoved) with
                        {
                            AttacksFiring = firing,
                            AttacksPotential = potential,
                        },
                    };
                }
            }
        }

        /// <summary>
        /// The forecast for one weapon-x-target pairing. <paramref name="hasCover"/> and
        /// <paramref name="ignoresCover"/> are caller-supplied facts (the option builder already
        /// computed both through the stages' own queries). Public for tests and for any later
        /// hypothetical-position caller (the CombatMath AttackContext pattern).
        /// </summary>
        public static AttackForecast Compute(IGameContext gameContext, DataBinding<UnitData> attackingUnit,
            Weapon weapon, DataBinding<UnitData> targetUnit, bool hasCover, bool ignoresCover,
            bool attackerMoved)
        {
            UnitData attacker = attackingUnit.GetValue();
            UnitData defender = targetUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender,
                out _, out _, includeVertical: true);
            List<ITerrain> terrain = gameContext.TableState.Terrain.Objects.ToList();

            RuleParticipant actor = RuleParticipant.Actor(attacker, weapon,
                HeroStatRules.LivingWeaponBatchOwners(attacker, weapon), EModelRuleScope.AllOwners);
            RuleParticipant subject = RuleParticipant.Subject(defender,
                models: HeroStatRules.LivingModels(defender));

            // --- DetermineHitRollStage mirror (same context, participants and sinks; read-only).
            IReadOnlyList<(RuleOperation Op, string RuleName)> hitNamed =
                gameContext.RuleEvaluator.EvaluateAllNamed(
                    new HitRollModifierContext(attacker, defender, distance,
                        AttackerMoved: attackerMoved, TerrainPieces: terrain),
                    actor, subject);
            List<RuleOperation> hitOps = hitNamed.Select(n => n.Op).ToList();

            int baseQuality = HeroStatRules.GetAttackQuality(attacker, weapon);
            QualityFloorSink qualityFloor = new QualityFloorSink();
            qualityFloor.ApplyFrom(hitOps);
            if (qualityFloor.HasFloor)
            {
                baseQuality = Math.Min(baseQuality, qualityFloor.Quality);
            }

            RollModifierSink hitModifiers = new RollModifierSink();
            hitModifiers.ApplyFrom(hitOps);

            int grantedHit = GrantedRollModifiers.PeekNet(attacker, ERollKind.Hit);
            int persistentHitMarkers = defender.Tokens.GetTokenCount(TokenType.PersistentHitBonus);

            int hitNeeded = baseQuality - hitModifiers.Net(ERollKind.Hit) - grantedHit
                - persistentHitMarkers;
            List<string>? hitTags = DetermineHitRollStage<ICombatMetadata>.ComposeThresholdTags(
                baseQuality, hitNamed, grantedHit, fatiguedInMelee: false, persistentHitMarkers);

            // --- RollToHitStage's whole-attack fold, evaluated against an EMPTY histogram:
            // unconditional save modifiers (Shielded, Fortified, a defensive aura) contribute; a
            // face-triggered op reads zero natural 6s and contributes nothing - which is exactly what
            // a pre-roll number wants.
            IReadOnlyList<(RuleOperation Op, string RuleName)> completeNamed =
                gameContext.RuleEvaluator.EvaluateAllNamed(
                    new HitRollCompleteContext(attacker, defender, EmptyRolls(), distance,
                        TerrainPieces: terrain),
                    actor, subject);
            List<RuleOperation> completeOps = completeNamed.Select(n => n.Op).ToList();

            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(completeOps);
            int apReduction = completeOps.OfType<RuleOperation.ReduceArmorPenetration>()
                .Sum(op => op.Amount);
            List<string>? saveModifierTags =
                RollToHitStage<ICombatMetadata>.ComposeSaveModifierTags(completeNamed);

            // --- DetermineSaveRollsNeededStage mirror (line-for-line sign conventions).
            int baseDefense = HeroStatRules.GetSaveDefense(defender);
            int ap = Math.Max(0, weapon.ArmorPenetration - apReduction);
            int coverBonus = hasCover && !ignoresCover ? 1 : 0;
            int grantedDefense = GrantedRollModifiers.PeekNet(defender, ERollKind.Save);
            int persistentApMarkers = defender.Tokens.GetTokenCount(TokenType.PersistentApBonus);

            int saveNeeded = baseDefense + ap - coverBonus - saveModifiers.Net(ERollKind.Save)
                - grantedDefense + persistentApMarkers;
            List<string>? saveTags = DetermineSaveRollsNeededStage<ICombatMetadata>.ComposeThresholdTags(
                baseDefense, ap, coverBonus, saveModifierTags, grantedDefense, persistentApMarkers);

            return new AttackForecast(
                DiceUtilities.ClampSuccessRollNeeded(hitNeeded),
                DiceUtilities.ClampSuccessRollNeeded(saveNeeded),
                hitTags, saveTags, ComposeNotes(defender));
        }

        // Roll-time facts that exist as visible tokens on the defender right now, phrased for the
        // details pane. ASCII-only by project convention.
        private static List<string>? ComposeNotes(UnitData defender)
        {
            List<string> notes = new List<string>();

            int spendableHit = defender.Tokens.GetTokenCount(TokenType.SpendableHitBonus);
            if (spendableHit > 0)
            {
                notes.Add($"Target carries {spendableHit} spendable marker(s): up to " +
                    $"+{spendableHit} to hit, claimed at roll time.");
            }

            int spendableAp = defender.Tokens.GetTokenCount(TokenType.SpendableApBonus);
            if (spendableAp > 0)
            {
                notes.Add($"Target carries {spendableAp} spendable marker(s): up to " +
                    $"AP(+{spendableAp}), claimed at roll time.");
            }

            if (defender.Tokens.GetAllTokens(TokenType.Mark).Any())
            {
                notes.Add("Mark on target: the first attack into it claims the marked bonus at roll time.");
            }

            return notes.Count > 0 ? notes : null;
        }

        private static IDiceResults EmptyRolls() =>
            new DiceResults(new float[IDiceRollerExtensions.DEFAULT_SIDE_COUNT]);
    }
}
