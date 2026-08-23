using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Utilities;

namespace FDG.Stages
{
    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, ICombatMetadata>
    {
        public CoverCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<CoverCheckResults, Task> onFinished)
        {
            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                GameContext.TableState, metaData.AttackingUnit, metaData.DefendingUnit,
                GameContext.Settings.SeeThroughFriendlyUnits);
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects
                .Concat(modelBlockers).ToList();

            // #385: the shared majority computation - the same function that feeds the targeting
            // panel's cover flag, so the preview and this roll cannot disagree. Dead models are
            // excluded on both sides there (#158); this stage used to count them. The #201
            // proximity exceptions ride the same call.
            CoverMajority.Result majority = CoverMajority.Evaluate(
                metaData.AttackingUnit, metaData.DefendingUnit, terrain,
                GameContext.Settings.CoverProximityExceptionsEnabled);

            // Majority rule: more than half of the living defending models in cover → +1 defense bonus.
            int bonus = majority.HasCover ? 1 : 0;

            // #042 Blast: the attacking weapon ignores cover — drop the bonus. Derived from the attacker's
            // rules via the shared query so the cover stage, targeting options, and movement options agree.
            if (bonus > 0 && SightRuleQueries.IgnoresCover(
                    metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator))
            {
                GameContext.Log($"Cover ignored by {metaData.AttackingUnit.GetValue().Name}'s weapon (Blast).");
                bonus = 0;
            }

            if (bonus > 0)
                GameContext.Log($"Cover: {majority.ModelsInCover}/{majority.LivingDefenders} defending models in cover. Defense +{bonus}.");
            else
                GameContext.Log($"Cover: {majority.ModelsInCover}/{majority.LivingDefenders} defending models in cover. No bonus.");

            await onFinished(new CoverCheckResults(bonus));
        }
    }
}
