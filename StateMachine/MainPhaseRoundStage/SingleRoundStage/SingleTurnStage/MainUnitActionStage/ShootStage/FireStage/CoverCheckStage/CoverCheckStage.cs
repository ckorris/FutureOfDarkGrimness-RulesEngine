using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, ICombatMetadata>
    {
        public CoverCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Action<CoverCheckResults> onFinished)
        {
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects.ToList();

            List<DataBinding<ModelData>> attackers = metaData.AttackingUnit.ModelBindings().ToList();
            List<DataBinding<ModelData>> defenders = metaData.DefendingUnit.ModelBindings().ToList();

            int modelsInCover = 0;
            foreach (DataBinding<ModelData> defender in defenders)
            {
                Position defPos = defender.GetValue().PositionBinding.GetValue();
                foreach (DataBinding<ModelData> attacker in attackers)
                {
                    ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
                        attacker.GetValue().PositionBinding.GetValue(), defPos, terrain);
                    if (effect == ESightLineEffect.Cover)
                    {
                        modelsInCover++;
                        break;
                    }
                }
            }

            // Majority rule: more than half of defending models in cover → +1 defense bonus.
            int bonus = modelsInCover * 2 > defenders.Count ? 1 : 0;

            if (bonus > 0)
                GameContext.Log($"Cover: {modelsInCover}/{defenders.Count} defending models in cover. Defense +{bonus}.");
            else
                GameContext.Log($"Cover: {modelsInCover}/{defenders.Count} defending models in cover. No bonus.");

            onFinished(new CoverCheckResults(bonus));
        }
    }
}
