using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Shooting target choice by value-weighted CombatMath (#191 A4-3) instead of the solo bot's
    /// most-shooters-not-in-cover count: for every selectable (weapon, target) pair, estimate the
    /// volley's expected wounds with the engine's own rule evaluation and weigh them by how much of
    /// the target's worth they remove - so a 1.2-wound volley into a fragile caster outranks a
    /// 2-wound volley soaked by a 9-wound tank. Kills are worth extra (a dead unit stops acting).
    /// </summary>
    public class TacticianRangedAttackResolver
        : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>
    {
        private readonly RuleEvaluator _evaluator;
        private readonly ITableState? _tableState;
        // #376 (Grounded Speed): terrain snapshot for the mobility queries - see TacticianPlanner.
        // Null when no table state was supplied (bare-resolver tests): terrain-gated movement rules
        // then conservatively don't fire in the threat estimate.
        private IReadOnlyList<ITerrain>? _terrainSnapshot;
        private IReadOnlyList<ITerrain>? Terrain =>
            _terrainSnapshot ??= _tableState == null ? null : TacticalAnalysis.TerrainOf(_tableState);
        private readonly IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> _soloFallback;

        public TacticianRangedAttackResolver(RuleEvaluator evaluator,
            IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> soloFallback,
            ITableState? tableState = null)
        {
            _evaluator = evaluator;
            _tableState = tableState;
            _soloFallback = soloFallback;
        }

        public Task<CancellableResult<RangedAttackChoice>> Resolve(ChooseRangedAttackRequest request)
        {
            Weapon? bestWeapon = null;
            WeaponTargetStats? bestStats = null;
            float bestScore = float.NegativeInfinity;

            foreach (WeaponOption option in request.WeaponOptions)
            {
                foreach (WeaponTargetStats stats in option.WeaponTargetStats)
                {
                    if (stats.UnselectableReason != null || stats.modelsThatCanShoot.Count == 0) continue;

                    UnitData target = stats.TargetUnit.GetValue();
                    float distance = MinDistance(stats.modelsThatCanShoot, target);
                    // #442 (#432): the hit stages and the ranged morale test measure the attack's distance unit to
                    // unit, closest living models, base to base (DetermineHitRollStage, RegisterAttackedDefender) -
                    // so the "shot from over 9"" arms (Stealth, Devout, Hive Bond, ...) price on the right side.
                    float attackDistance = UnitCompareUtilities.MinDistanceBetweenUnits(
                        request.AttackingUnit.GetValue(), target, out _, out _, includeVertical: true);
                    (float wounds, float counted) = CombatMath.EstimateVolleyWithCounted(_evaluator,
                        request.AttackingUnit, stats.TargetUnit, option.Weapon, stats.modelsThatCanShoot.Count,
                        new AttackContext(attackDistance, AttackerMoved: true, DefenderInCover: stats.HasCover),
                        new List<string>());

                    float remaining = Math.Max(1f, target.RemainingWounds);
                    float fractionKilled = Math.Min(1f, wounds / remaining);
                    // A5-4 mob breaking: a volley that pushes the unit below HALF strength is
                    // worth extra beyond its wounds - half-strength morale tests rout whole mobs
                    // (the engine's own mechanic), so breaking a horde beats shaving it.
                    int living = target.Models.Count(m => m.GetIsAlive());
                    // #442 (#431 Scourge): the half-strength morale check allocates counted wounds virtually, so
                    // they can push the mob over the line (and make the test due) without being dealt.
                    float kills = CombatMath.ExpectedKillsFrom(target, wounds + counted);
                    bool breaks = living * 2 > target.Models.Count
                        && (living - kills) * 2f <= target.Models.Count;
                    // A5-6: a loaded transport is worth boat + payload (destroying it spills the
                    // cargo out Shaken), and a target that can charge US next activation is worth
                    // killing before one that cannot reach us.
                    float targetValue = _tableState != null
                        ? TacticalAnalysis.UnitValueWithCargo(target, _tableState, _evaluator)
                        : TacticalAnalysis.UnitValue(target);
                    // #355: a unit that can only ram still threatens to charge us.
                    // #442 (#425): a target that may not charge (Cumbersome) does not threaten us in melee.
                    bool threatensUs = ChargeContactRules.CanFightInMelee(target)
                        && TacticalAnalysis.MayCharge(target, _evaluator)
                        && TacticalAnalysis.MeleeThreatReach(target,
                            request.AttackingUnit.GetValue(), _evaluator, Terrain) >= distance - 1f;
                    // #442 (#421): breaking a mob is worth the bonus only as often as it actually fails the test -
                    // a target testing at its Tough(9)+ Hero's Quality, re-rolling with Fearless or holding with Hive
                    // Bond's +1 is correspondingly less attractive to break.
                    float breakBonus = breaks && wounds < remaining && _tableState != null
                        ? 1f + (TacticianWeights.MoraleBreakBonus - 1f) * (1f - TacticalAnalysis.MoralePassChance(
                            _tableState, _evaluator, target, Rules.Foundation.EMoraleTestCause.Shooting,
                            shotFromInches: attackDistance))
                        : TacticianWeights.MoraleBreakBonus;
                    float score = fractionKilled * targetValue
                        * (wounds >= remaining ? TacticianWeights.ShootingKillBonus
                           : breaks ? breakBonus : 1f)
                        * (threatensUs ? TacticianWeights.ShootThreatFactor : 1f);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWeapon = option.Weapon;
                        bestStats = stats;
                    }
                }
            }

            if (bestWeapon == null || bestStats == null)
                return _soloFallback.Resolve(request); // nothing selectable: solo's graceful paths apply

            return Task.FromResult<CancellableResult<RangedAttackChoice>>(
                new Selected<RangedAttackChoice>(new RangedAttackChoice(bestWeapon, bestStats.TargetUnit)));
        }

        private static float MinDistance(
            IReadOnlyCollection<DataBinding<ModelData>> shooters, UnitData target)
        {
            float best = float.MaxValue;
            foreach (DataBinding<ModelData> shooter in shooters)
            {
                Position from = shooter.GetValue().Position;
                foreach (IModel enemyModel in target.Models)
                {
                    if (!enemyModel.GetIsAlive()) continue;
                    float dx = from.x - enemyModel.Position.x, dz = from.z - enemyModel.Position.z;
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d < best) best = d;
                }
            }
            return best == float.MaxValue ? 1f : Math.Max(1f, best);
        }
    }
}
