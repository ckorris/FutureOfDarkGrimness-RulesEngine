using System;
using System.Linq;
using System.Text;
using FDG.Data;
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

            // #029: an Aircraft's Advance is a forced straight-line 30-36" move along its fixed heading (no
            // free-form path), so it bypasses the normal request + validation entirely.
            if (Rules.Dispatch.AircraftRules.IsAircraft(context.MovingUnit.GetValue()))
            {
                await ResolveForcedAircraftMove(context, playerID);
                return;
            }

            // #029: a defender's Melee-Shrouding-style charge penalty shrinks how far the charger may move to
            // reach it. A charge's target isn't pinned until the move ends, so apply the worst case among
            // enemies actually in charge reach — conservative, but never lets an over-long charge through.
            float effectiveCharge = WorstCaseChargeDistance(context);
            float hardCap = System.Math.Max(context.MaxRushDistance, effectiveCharge);

            // #093: per-model budgets so the resolvers can preview/clamp a joined hero's Fast/Slow against
            // its own reach. The unit-level charge shrink (Melee Shrouding) applies to every model's charge
            // equally; a model's hard cap is max(its Rush, its shrunk Charge). A unit with no per-model
            // movement rules yields the unit scalars for every model.
            float chargeShrink = context.MaxChargeDistance - effectiveCharge;
            var perModelBudgets = new List<ModelMoveBudgetInfo>();
            foreach (IModel model in context.MovingUnit.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;
                context.TryGetModelMoveBudget(model, out float advance, out float rush, out float charge);
                perModelBudgets.Add(new ModelMoveBudgetInfo(model.ID, advance, rush,
                    System.Math.Max(rush, charge - chargeShrink)));
            }

            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);
            bool ignoresImpassibleTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator);

            var pathRequest = new DefineMovementPathRequest(playerID, "Move Unit", context.MovingUnit,
                context.MaxAdvanceDistance, context.MaxRushDistance, hardCap,
                WeaponSightProfileBuilder.For(context.MovingUnit.GetValue(), context.GameContext.RuleEvaluator),
                canMoveThroughEnemies, ignoresDifficultTerrain, ignoresImpassibleTerrain, BuildRangeOverrides(context),
                perModelBudgets);

            List<ModelMoveEntry> movements = await context.PlayerRequester()
                .RequestDecision<DefineMovementPathRequest, List<ModelMoveEntry>>(pathRequest);

            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(context.MovingUnit, context.GameContext);

            // #093: validate each model against its OWN budget — the same per-model budgets the request
            // carried to the resolver, so the move preview and the authoritative check agree exactly.
            if(MovementUtilities.ValidatePaths(movements,
                entry => { var (_, rush, maxDist) = pathRequest.BudgetFor(entry.Model.GetValue().ID);
                           return new ModelMoveBudget(rush, maxDist); },
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

        // #029: turn an Aircraft's Advance into a forced straight-line move along its fixed heading. The player
        // picks the distance (30–36"); the direction can't change while it's on the table. If the move carries it
        // off the table edge it leaves play (held off-table) and redeploys from an edge next round; otherwise the
        // straight move is committed. Aircraft ignore terrain + units, so there's nothing to validate against.
        private async Task ResolveForcedAircraftMove(IMovementActionContext context, PlayerID playerID)
        {
            UnitData unit = context.MovingUnit.GetValue();
            Float2 heading = ForcedAircraftMove.EnsureHeading(unit);

            float distance = await ChooseForcedDistance(context, playerID, unit.Name);
            List<ModelMoveEntry> paths = ForcedAircraftMove.BuildPaths(context.MovingUnit, heading, distance);

            if (ForcedAircraftMove.WouldLeaveTable(paths))
            {
                // Flew off the edge: leave play (models to origin), mark for redeploy, and clear the heading so it
                // re-aims when re-placed next round. The activation then has nothing more to do off-table.
                List<ModelMoveEntry> hold = new List<ModelMoveEntry>();
                foreach (DataBinding<ModelData> model in unit.ModelBindings)
                {
                    if (!model.GetValue().GetIsAlive()) continue;
                    model.GetValue().SetPosition(new Position(0f, 0f));
                    hold.Add(new ModelMoveEntry(model, new List<Position> { new Position(0f, 0f) }));
                }
                unit.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.OffTableFromForcedMove, 1,
                    new Rules.Foundation.TokenClearTrigger.ManualOnly()));
                unit.AircraftHeading = null;
                context.GameContext.Log($"{unit.Name} (Aircraft) flew off the table edge — it redeploys from an edge next round.");

                context.SubmitValidPathTemplate(hold);
                await OnPathDefined.Activate(context);
                return;
            }

            context.GameContext.Log($"{unit.Name} (Aircraft) advances {distance}\" in a straight line along its heading.");
            context.SubmitValidPathTemplate(paths);
            await OnPathDefined.Activate(context);
        }

        // Offers the Aircraft's forced-move distance (30/33/36"). The AI / EOF default picks the first (30").
        private async Task<float> ChooseForcedDistance(IMovementActionContext context, PlayerID playerID, string unitName)
        {
            List<string> options = ForcedAircraftMove.DistancesInches.Select(d => $"{d:0.#}\"").ToList();

            var request = new StringSelectionRequest(playerID,
                $"How far does {unitName} (Aircraft) fly straight ahead?",
                options, new List<StringSelectionRequest.InvalidOption>());
            string chosen = await context.PlayerRequester().RequestDecision<StringSelectionRequest, string>(request);

            for (int i = 0; i < ForcedAircraftMove.DistancesInches.Length; i++)
                if (options[i] == chosen) return ForcedAircraftMove.DistancesInches[i];
            return ForcedAircraftMove.DistancesInches[0];
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
