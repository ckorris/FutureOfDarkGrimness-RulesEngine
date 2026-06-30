using System;
using System.Text;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public class DefinePathStage : StageBase<IMovementActionContext>
    {
        public StageBinding OnPathDefined;

        public DefinePathStage(IGameContext gameContext, IStateMachineLayer<IMovementActionContext> parent)
            : base(gameContext, parent)
        {
            OnPathDefined = new StageBinding(this);
        }

        public override async Task Enter(IMovementActionContext context)
        {

            PlayerID playerID = context.MovingUnit.GetValue().PlayerID; //Shorthand.

            // #029: a defender's Melee-Shrouding-style charge penalty shrinks how far the charger may move to
            // reach it. A charge's target isn't pinned until the move ends, so apply the worst case among
            // enemies actually in charge reach — conservative, but never lets an over-long charge through.
            float effectiveCharge = WorstCaseChargeDistance(context);
            float hardCap = System.Math.Max(context.MaxRushDistance, effectiveCharge);

            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresImpassibleTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);

            var pathRequest = new DefineMovementPathRequest(playerID, "Move Unit", context.MovingUnit,
                context.MaxAdvanceDistance, context.MaxRushDistance, hardCap,
                WeaponSightProfileBuilder.For(context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator),
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, BuildRangeOverrides(context));

            List<ModelMoveEntry> movements = await context.PlayerRequester()
                .RequestDecision<DefineMovementPathRequest, List<ModelMoveEntry>>(pathRequest);

            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(context.MovingUnit, context.GameContext);

            if(MovementUtilities.ValidatePaths(movements, context.MaxRushDistance, hardCap,
                enemyFootprints, canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain,
                context.RelevantTerrain, out List<ReasonForInvalidMove> invalidReasons) == false)
            {
                StringBuilder sb = new StringBuilder(invalidReasons[0].ToString());
                for(int i = 1; i < invalidReasons.Count; i++)
                {
                    sb.Append(", " + invalidReasons[i].ToString());
                }

                throw new RequestResponseInvalidException($"Response to {nameof(DefinePathStage)} movement request was invalid for the following reasons: "
                    + sb.ToString());
            }

            context.SubmitValidPathTemplate(movements);

            await OnPathDefined.Activate(context);
        }

        // #102: precompute, per (mover's ranged weapon × on-table enemy unit), the effective shooting range
        // once range-modifier rules fold in (Increased Shooting Range widens, Ranged Shrouding narrows). The
        // movement "Show targeting" overlay reads these so its post-move shooting preview matches the
        // authoritative ChooseRangedAttackStage range check. Only pairs that differ from the weapon's base
        // range are emitted (a missing pair means "use the base range"); empty when no range rule is in play.
        private static List<WeaponRangeOverride> BuildRangeOverrides(IMovementActionContext context)
        {
            IUnit attacker = context.MovingUnit.GetValue();
            List<Weapon> ranged = attacker.GetRangedWeapons();
            if (ranged.Count == 0) return new List<WeaponRangeOverride>();

            Dictionary<string, Weapon> distinctByName = new Dictionary<string, Weapon>();
            foreach (Weapon weapon in ranged) distinctByName.TryAdd(weapon.Name, weapon);

            var evaluator = context.GameContext.RuleEvaluator;
            List<WeaponRangeOverride> overrides = new List<WeaponRangeOverride>();
            foreach (IUnit enemy in context.GameContext.TableState.Units.Objects)
            {
                if (enemy.PlayerID == attacker.PlayerID) continue;
                if (!enemy.GetIsOnBattlefield()) continue;

                foreach (Weapon weapon in distinctByName.Values)
                {
                    float effective = Rules.Dispatch.RangeRuleQueries.EffectiveRange(attacker, weapon, enemy, evaluator);
                    if (System.Math.Abs(effective - weapon.RangeInches) > 0.001f)
                        overrides.Add(new WeaponRangeOverride(weapon.Name, enemy.ID, effective));
                }
            }
            return overrides;
        }

        // #029: the charge budget the charger may actually use, reduced by the strongest Melee-Shrouding-style
        // charge penalty among enemies within (base) charge reach. Conservative worst case: because a charge's
        // target isn't known until the move ends, one shrunk budget applies to the whole move — it never permits
        // an over-long charge into a shrouding unit, at the cost of also shortening charges toward a normal enemy
        // when a shrouding one is in range too. Returns the base charge when no charge penalty is in play.
        private static float WorstCaseChargeDistance(IMovementActionContext context)
        {
            IUnit charger = context.MovingUnit.GetValue();
            float baseCharge = context.MaxChargeDistance;
            var evaluator = context.GameContext.RuleEvaluator;

            float worst = baseCharge;
            foreach (IUnit enemy in context.GameContext.TableState.Units.Objects)
            {
                if (enemy.PlayerID == charger.PlayerID) continue;
                if (!enemy.GetIsOnBattlefield()) continue;
                // A shrouding unit beyond base charge reach can't be charged anyway, so it must not shrink the budget.
                if (MinBaseToBaseDistanceInches(charger, enemy) > baseCharge) continue;

                float effective = Rules.Dispatch.MovementRuleQueries.EffectiveChargeDistanceAgainst(
                    charger, enemy, baseCharge, evaluator);
                if (effective < worst) worst = effective;
            }
            return worst;
        }

        private static float MinBaseToBaseDistanceInches(IUnit a, IUnit b)
        {
            float min = float.MaxValue;
            foreach (IModel modelA in a.Models)
            {
                if (!modelA.GetIsAlive()) continue;
                foreach (IModel modelB in b.Models)
                {
                    if (!modelB.GetIsAlive()) continue;
                    float distance = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        modelA.Position, modelB.Position, modelA.BaseRadiusInches, modelB.BaseRadiusInches);
                    if (distance < min) min = distance;
                }
            }
            return min;
        }

    }
}
