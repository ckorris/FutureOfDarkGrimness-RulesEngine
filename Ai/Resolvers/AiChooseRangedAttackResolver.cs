using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// Picks a ranged attack target following the solo-rules priority:
    /// prefer targets not in cover; among equal candidates, prefer the one with the most shooters.
    /// </summary>
    public class AiChooseRangedAttackResolver : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>
    {
        public Task<RangedAttackChoice> Resolve(ChooseRangedAttackRequest request)
        {
            // Prefer options where at least one model has LOS and is in range.
            var options = BuildOptions(request, requireShooters: true);

            // Fall back to all options if LOS blocks every shot (engine will cancel gracefully).
            if (options.Count == 0)
                options = BuildOptions(request, requireShooters: false);

            if (options.Count == 0)
                throw new InvalidOperationException("AI received a ranged attack request with no options at all.");

            var best = options
                .OrderBy(o => o.stats.HasCover ? 1 : 0)
                .ThenByDescending(o => o.stats.modelsThatCanShoot.Count)
                .First();

            return Task.FromResult(new RangedAttackChoice(best.weapon, best.stats.TargetUnit));
        }

        private static List<(Weapon weapon, WeaponTargetStats stats)> BuildOptions(
            ChooseRangedAttackRequest request, bool requireShooters)
        {
            var result = new List<(Weapon, WeaponTargetStats)>();
            foreach (var weaponOption in request.WeaponOptions)
                foreach (var targetStats in weaponOption.WeaponTargetStats)
                    if (!requireShooters || targetStats.modelsThatCanShoot.Count > 0)
                        result.Add((weaponOption.Weapon, targetStats));
            return result;
        }
    }
}
