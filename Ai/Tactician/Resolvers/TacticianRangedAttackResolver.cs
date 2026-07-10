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
        private readonly IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> _soloFallback;

        public TacticianRangedAttackResolver(RuleEvaluator evaluator,
            IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>> soloFallback)
        {
            _evaluator = evaluator;
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
                    float wounds = CombatMath.EstimateVolley(_evaluator, request.AttackingUnit,
                        stats.TargetUnit, option.Weapon, stats.modelsThatCanShoot.Count,
                        new AttackContext(distance, AttackerMoved: true, DefenderInCover: stats.HasCover),
                        new List<string>());

                    float remaining = Math.Max(1f, target.RemainingWounds);
                    float fractionKilled = Math.Min(1f, wounds / remaining);
                    float score = fractionKilled * TacticalAnalysis.UnitValue(target)
                        * (wounds >= remaining ? TacticianWeights.ShootingKillBonus : 1f);

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
