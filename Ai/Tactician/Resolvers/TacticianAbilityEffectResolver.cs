using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #442 (goal item 5, #434's Versatile Expert / Warrior): the "pick one effect" activation choice - Versatile
    /// Attack / Warrior / Expert, Watchborn, Vinci Tech: AP(+1) or +1 to hit for this activation - by what each pick
    /// is WORTH against the best target this unit can attack this activation, instead of solo's first option.
    /// <para>
    /// Each option's ability grants a helper rule (<see cref="Effect.AddRule"/>); the helper's definition comes from
    /// <see cref="TacticianRuleLookup"/> and is priced through CombatMath as a hypothetical attacker rule, so every
    /// condition the helper carries (Versatile Warrior's "over 9"/6"", the Fatigue gates, the target's rules) prices
    /// exactly as the real grant would resolve. Value = the target's value fraction removed, the planner's currency;
    /// the target set is every enemy the unit can shoot (advancing if it must) or charge this activation.
    /// </para>
    /// <para>
    /// Falls back to the solo answer (the first option) whenever it cannot price honestly: the bearer is not the
    /// unit the planner is activating (the deployment-time copy), a helper does not resolve, or every option prices
    /// the same (Versatile Reach's range/charge picks and Versatile Defense's defensive ones never change an attack
    /// estimate at a fixed distance, so they keep the first option, as before). Never replies Cancel.
    /// </para>
    /// </summary>
    public class TacticianAbilityEffectResolver : IStageResolver<ChooseAbilityEffectRequest, int>
    {
        private const float TieEpsilon = 0.0001f;

        private readonly TacticianPlanner _planner;
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        private readonly TacticianRuleLookup _rules;
        private readonly IStageResolver<ChooseAbilityEffectRequest, int> _soloFallback;
        private readonly Action<string>? _log;

        public TacticianAbilityEffectResolver(TacticianPlanner planner, ITableState tableState,
            RuleEvaluator evaluator, IStageResolver<ChooseAbilityEffectRequest, int> soloFallback,
            Action<string>? log = null)
        {
            _planner = planner;
            _tableState = tableState;
            _evaluator = evaluator;
            _rules = new TacticianRuleLookup(tableState);
            _soloFallback = soloFallback;
            _log = log;
        }

        public Task<int> Resolve(ChooseAbilityEffectRequest request)
        {
            int? choice = Choose(request);
            return choice.HasValue ? Task.FromResult(choice.Value) : _soloFallback.Resolve(request);
        }

        private int? Choose(ChooseAbilityEffectRequest request)
        {
            DataBinding<UnitData>? unit = _planner.ActiveUnit;
            if (unit == null || unit.GetValue().Name != request.BearerName || request.Options.Count < 2) return null;

            ResolvedRule? offering = FindRule(unit.GetValue(), request.RuleName);
            if (offering == null) return null;

            var values = new float[request.Options.Count];
            for (int i = 0; i < request.Options.Count; i++)
            {
                string label = request.Options[i].Label;
                ActivatedAbility? ability = offering.Definition.Activated.FirstOrDefault(a => a.Label == label);
                if (ability?.Effect is not Effect.AddRule addRule) return null;
                if (!_rules.TryResolve(addRule.RuleName, out ResolvedRule helper)) return null;
                values[i] = BestTargetValue(unit, helper);
            }

            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (values[i] > values[best] + TieEpsilon) best = i;
            if (values.Max() - values.Min() <= TieEpsilon) best = 0; // nothing to choose between: the authored default

            _log?.Invoke($"pick {request.RuleName} for {unit.GetValue().Name} -> {request.Options[best].Label} (" +
                string.Join(", ", request.Options.Select((o, i) => $"{o.Label}={values[i]:F3}")) + ")");
            return best;
        }

        // The offering rule by the name the request carries: the unit's own rules, then its living models' (a joined
        // hero's rules live on its model after the #006 merge).
        private static ResolvedRule? FindRule(UnitData unit, string ruleName)
        {
            foreach (ResolvedRule rule in unit.RuleDefinitions)
                if (rule.RequestedName == ruleName || rule.Definition.Name == ruleName) return rule;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (ResolvedRule rule in model.RuleDefinitions)
                    if (rule.RequestedName == ruleName || rule.Definition.Name == ruleName) return rule;
            }
            return null;
        }

        // The best value fraction (planner currency) this unit removes from any one enemy this activation with the
        // helper rule hypothetically in play: a shot from where it stands, or after an Advance when the target is out
        // of range, and a charge when the target is inside its melee threat reach.
        private float BestTargetValue(DataBinding<UnitData> unitBinding, ResolvedRule helper)
        {
            UnitData self = unitBinding.GetValue();
            IReadOnlyList<ITerrain> terrain = TacticalAnalysis.TerrainOf(_tableState);
            float advance = TacticalAnalysis.AdvanceDistance(self, _evaluator, terrain);
            bool shootsAfterMoving = Rules.Dispatch.ActionRestrictionQueries.ShootingAfterMovingForbiddenBy(
                self, _evaluator) == null;
            bool mayCharge = self.GetMeleeWeapons().Count > 0
                && Rules.Dispatch.ActionRestrictionQueries.MayDeclare(self, _evaluator, EActionType.Charge);

            float best = 0f;
            foreach (IArmy army in _tableState.Armies.Objects)
            {
                if (army is not ArmyData data || TacticalAnalysis.AreAllied(_tableState, self.PlayerID, data.PlayerID))
                    continue;
                foreach (DataBinding<UnitData> enemyBinding in data.UnitBindings)
                {
                    UnitData enemy = enemyBinding.GetValue();
                    if (!enemy.GetIsAlive() || !enemy.GetIsOnBattlefield()) continue;
                    float distance = UnitCompareUtilities.MinDistanceBetweenUnits(self, enemy, out _, out _,
                        includeVertical: true);

                    float range = TacticalAnalysis.MaxWeaponRange(self, enemy, _evaluator);
                    bool mustMove = distance > range;
                    if (range > 0f && (!mustMove || (shootsAfterMoving && distance <= range + advance)))
                    {
                        AttackEstimate shot = CombatMath.EstimateShooting(_evaluator, unitBinding, enemyBinding,
                            new AttackContext(Math.Max(1f, mustMove ? distance - advance : distance),
                                AttackerMoved: mustMove), helper);
                        best = Math.Max(best, ValueFraction(shot.ExpectedWounds, enemy));
                    }

                    if (mayCharge && distance <= TacticalAnalysis.MeleeThreatReach(self, enemy, _evaluator, terrain))
                    {
                        MeleeEstimate melee = CombatMath.EstimateMelee(_evaluator, unitBinding, enemyBinding,
                            chargeOriginInches: distance, hypotheticalAttackerRule: helper);
                        best = Math.Max(best, ValueFraction(melee.AttackerAttack.ExpectedWounds, enemy));
                    }
                }
            }
            return best;
        }

        private static float ValueFraction(float expectedWounds, UnitData target)
        {
            float remaining = Math.Max(1f, target.RemainingWounds);
            return Math.Min(1f, expectedWounds / remaining) * TacticalAnalysis.UnitValue(target) / 100f;
        }
    }
}
