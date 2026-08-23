using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// Builds the model positions an <c>AttackBeat</c> animates between. Firing positions are the
    /// actual weapon-carrying models — matched against the firing weapon type — so a mixed unit
    /// (e.g. one rifle among five models) shows the shot from the right model, one entry per matching
    /// weapon instance. #276 keeps them honest: only carriers that can actually strike are shown —
    /// ranged carriers need line of sight (unless the weapon ignores it) and effective range to some
    /// living defender model, melee carriers must be in melee range — and the list is then capped at
    /// the attack's true weapon count, rotated by the shot's burst index so a #157 split volley fires
    /// each shot from a different carrier. Target positions are the defender's alive, on-table models
    /// (a Takedown shot aims at its picked model). Models held in reserve (Ambush) sit at the origin
    /// and are excluded.
    /// </summary>
    internal static class AttackBeatPositions
    {
        /// <summary>The From/To endpoints for <paramref name="metaData"/>'s attack beat.</summary>
        public static (List<Position> From, List<Position> To) Endpoints(ITableState tableState,
            ICombatMetadata metaData, RuleEvaluator evaluator, bool seeThroughFriendlyUnits)
        {
            // Non-consuming query (BuildTargetListStage's own evaluation drives the actual rule), so
            // reading it here for both the shooter filter and the target side is safe.
            bool ignoresLineOfSight = !metaData.IsMelee && SightRuleQueries.IgnoresTerrain(
                metaData.AttackingUnit.GetValue(), metaData.WeaponType, evaluator);

            List<Position> from = metaData.IsMelee
                ? MeleeFiringModels(metaData.AttackingUnit, metaData.DefendingUnit, metaData.WeaponType)
                : RangedFiringModels(tableState, metaData.AttackingUnit, metaData.DefendingUnit,
                    metaData.WeaponType, evaluator, ignoresLineOfSight, seeThroughFriendlyUnits);

            from = SelectBurstShooters(from, metaData.WeaponCount, metaData.BurstShotIndex);

            List<Position> to;
            if (metaData.IsMelee)
            {
                // Melee is adjacent — LoS is moot and the overlay clashes at the nearest model.
                to = AlivePlaced(metaData.DefendingUnit);
            }
            else if (metaData.QueryForResult(out IndividualTargetResult individualTarget)
                && individualTarget.Model.GetIsAlive())
            {
                // A Takedown shot already picked its victim (BuildTargetListStage runs before the
                // roll) — the tracer aims at that model, not a spread across the unit.
                to = new List<Position> { individualTarget.Model.GetValue().PositionBinding.GetValue() };
            }
            else if (ignoresLineOfSight)
            {
                // An ignore-LoS weapon (Indirect) may lob at models it can't see.
                to = AlivePlaced(metaData.DefendingUnit);
            }
            else
            {
                to = VisibleTargets(tableState, metaData.AttackingUnit, metaData.DefendingUnit, from,
                    seeThroughFriendlyUnits);
            }

            return (from, to);
        }

        public static List<Position> FiringModels(DataBinding<UnitData> unit, IWeapon weaponType)
        {
            var comparer = new WeaponComparer();
            var positions = new List<Position>();
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;
                foreach (Weapon weapon in model.Weapons)
                {
                    if (comparer.Equals(weapon, weaponType))
                        positions.Add(model.Position);
                }
            }
            return positions;
        }

        /// <summary>
        /// #276 ranged shooters: the carriers of <paramref name="weaponType"/> that could legally fire at
        /// <paramref name="defendingUnit"/> — line of sight (skipped when the weapon ignores it: Indirect,
        /// Takedown) and effective range to at least one living, placed defender model, mirroring the
        /// per-model eligibility the targeting stage computes. Falls back to all carriers if the filter
        /// would empty the list (the beat must show the shot the engine is resolving regardless).
        /// </summary>
        public static List<Position> RangedFiringModels(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit,
            IWeapon weaponType, RuleEvaluator evaluator, bool ignoresLineOfSight,
            bool seeThroughFriendlyUnits)
        {
            IUnit attacker = attackingUnit.GetValue();
            IUnit defender = defendingUnit.GetValue();
            float effectiveRange = RangeRuleQueries.EffectiveRange(attacker, weaponType, defender, evaluator);

            List<IModel> targets = AlivePlacedModels(defendingUnit);
            if (targets.Count == 0) return FiringModels(attackingUnit, weaponType);

            // Null blockers == the weapon ignores line of sight, which is exactly what ShotEligibility
            // reads a null list as.
            IReadOnlyList<ITerrain>? terrain = ignoresLineOfSight
                ? null
                : ShotEligibility.BuildBlockers(tableState, attackingUnit, defendingUnit,
                    seeThroughFriendlyUnits);

            var comparer = new WeaponComparer();
            var positions = new List<Position>();
            foreach (IModel model in attacker.Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;

                int copies = model.Weapons.Count(weapon => comparer.Equals(weapon, weaponType));
                if (copies == 0) continue;

                if (!CanModelShoot(model, targets, effectiveRange, terrain)) continue;

                for (int i = 0; i < copies; i++) positions.Add(model.Position);
            }

            return positions.Count > 0 ? positions : FiringModels(attackingUnit, weaponType);
        }

        // Mirrors ChooseRangedAttackStage.CanWeaponShootAtUnit: some living defender model must be both
        // seen (terrain == null means the weapon ignores LoS) and within effective range, base to base.
        // The test itself lives in ShotEligibility so the targeting previews ask the same question.
        private static bool CanModelShoot(IModel model, List<IModel> targets, float effectiveRangeInches,
            IReadOnlyList<ITerrain>? terrain)
            => ShotEligibility.CanHitAny(model.Position, model.BaseShape, model.Facing, targets, terrain,
                effectiveRangeInches);

        /// <summary>
        /// #276 melee strikers: the carriers of <paramref name="weaponType"/> that are in melee range of
        /// some living defender model — the same 2"/4" cylinder that gated the swing pool (#017), so a
        /// carrier standing off from the scrum doesn't animate a swing. Same empty-filter fallback as the
        /// ranged side.
        /// </summary>
        public static List<Position> MeleeFiringModels(DataBinding<UnitData> attackingUnit,
            DataBinding<UnitData> defendingUnit, IWeapon weaponType)
        {
            List<IModel> defenders = AlivePlacedModels(defendingUnit);
            if (defenders.Count == 0) return FiringModels(attackingUnit, weaponType);

            var comparer = new WeaponComparer();
            var positions = new List<Position>();
            foreach (IModel model in attackingUnit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;

                int copies = model.Weapons.Count(weapon => comparer.Equals(weapon, weaponType));
                if (copies == 0) continue;

                if (!defenders.Any(enemy => MeleeRangeUtilities.AreModelsInMeleeRange(model, enemy))) continue;

                for (int i = 0; i < copies; i++) positions.Add(model.Position);
            }

            return positions.Count > 0 ? positions : FiringModels(attackingUnit, weaponType);
        }

        /// <summary>
        /// #276: cap the firing list at the attack's true weapon count, rotated by the shot's position in
        /// its #157 split burst — shot k of a split Takedown volley fires from carrier k, so three sniper
        /// shots animate from three different snipers instead of all three at once, three times. Unsplit
        /// attacks (count covers the whole list) pass through untouched.
        /// </summary>
        public static List<Position> SelectBurstShooters(List<Position> positions, int weaponCount,
            int burstShotIndex)
        {
            if (weaponCount <= 0 || positions.Count <= weaponCount) return positions;

            var selected = new List<Position>(weaponCount);
            int start = (burstShotIndex * weaponCount) % positions.Count;
            for (int i = 0; i < weaponCount; i++)
            {
                selected.Add(positions[(start + i) % positions.Count]);
            }
            return selected;
        }

        public static List<Position> AlivePlaced(DataBinding<UnitData> unit)
        {
            var positions = new List<Position>();
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;
                positions.Add(model.Position);
            }
            return positions;
        }

        private static List<IModel> AlivePlacedModels(DataBinding<UnitData> unit)
        {
            var models = new List<IModel>();
            foreach (IModel model in unit.GetValue().Models)
            {
                if (!model.GetIsAlive() || IsAtOrigin(model.Position)) continue;
                models.Add(model);
            }
            return models;
        }

        /// <summary>
        /// The defender's alive, on-table models that at least one firing model can actually see
        /// (terrain + intervening enemy bases considered, same as the real LoS gate). Keeps the shot
        /// animation honest — we never draw a tracer at a model the unit couldn't legally target, even
        /// though the rules resolve damage at the unit level. Falls back to all alive models if the
        /// filter would be empty (the occlusion stage already guaranteed the unit can see *something*,
        /// and an empty target set would suppress the animation entirely).
        /// </summary>
        public static List<Position> VisibleTargets(ITableState tableState,
            DataBinding<UnitData> attackingUnit, DataBinding<UnitData> defendingUnit,
            IReadOnlyList<Position> firingPositions, bool seeThroughFriendlyUnits)
        {
            List<Position> targets = AlivePlaced(defendingUnit);
            if (firingPositions.Count == 0 || targets.Count == 0) return targets;

            IReadOnlyList<ITerrain> terrain = ShotEligibility.BuildBlockers(tableState, attackingUnit,
                defendingUnit, seeThroughFriendlyUnits);

            var visible = new List<Position>();
            foreach (Position target in targets)
            {
                foreach (Position shooter in firingPositions)
                {
                    if (LineOfSightUtilities.HasLineOfSight(shooter, target, terrain))
                    {
                        visible.Add(target);
                        break;
                    }
                }
            }
            return visible.Count > 0 ? visible : targets;
        }

        private static bool IsAtOrigin(Position pos) => pos.x == 0f && pos.z == 0f;
    }
}
