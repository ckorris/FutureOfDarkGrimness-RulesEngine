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
                GameContext.TableState, metaData.AttackingUnit, metaData.DefendingUnit);
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects
                .Concat(modelBlockers).ToList();

            List<DataBinding<ModelData>> attackers = metaData.AttackingUnit.ModelBindings().ToList();
            List<DataBinding<ModelData>> defenders = metaData.DefendingUnit.ModelBindings().ToList();

            // #201: the proximity exceptions (shooter hugging the piece / shared cover at knife
            // range) void individual sight-line cover when the lobby toggle is on.
            bool applyProximity = GameContext.Settings.CoverProximityExceptionsEnabled;

            int modelsInCover = 0;
            foreach (DataBinding<ModelData> defender in defenders)
            {
                ModelData defModel = defender.GetValue();
                Position defPos = defModel.PositionBinding.GetValue();
                foreach (DataBinding<ModelData> attacker in attackers)
                {
                    ModelData atkModel = attacker.GetValue();
                    ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
                        atkModel.PositionBinding.GetValue(), defPos, terrain,
                        CoverContext.ForModels(atkModel, defModel), applyProximity);
                    if (effect == ESightLineEffect.Cover)
                    {
                        modelsInCover++;
                        break;
                    }
                }
            }

            // Majority rule: more than half of defending models in cover → +1 defense bonus.
            int bonus = modelsInCover * 2 > defenders.Count ? 1 : 0;

            // #042 Blast: the attacking weapon ignores cover — drop the bonus. Derived from the attacker's
            // rules via the shared query so the cover stage, targeting options, and movement options agree.
            if (bonus > 0 && SightRuleQueries.IgnoresCover(
                    metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator))
            {
                GameContext.Log($"Cover ignored by {metaData.AttackingUnit.GetValue().Name}'s weapon (Blast).");
                bonus = 0;
            }

            if (bonus > 0)
                GameContext.Log($"Cover: {modelsInCover}/{defenders.Count} defending models in cover. Defense +{bonus}.");
            else
                GameContext.Log($"Cover: {modelsInCover}/{defenders.Count} defending models in cover. No bonus.");

            await onFinished(new CoverCheckResults(bonus));
        }
    }
}
