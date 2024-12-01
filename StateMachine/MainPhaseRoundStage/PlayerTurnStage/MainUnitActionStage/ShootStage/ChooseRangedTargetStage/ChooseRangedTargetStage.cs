
using System;
using System.Collections.Generic;

namespace FDG.Stages
{
    public class ChooseRangedTargetStage : StageBase<IRangedContext>
    {
        public const string CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION =
            "ChooseRangedTargetToFire";

        public StageBinding ToFire;
        public ChooseRangedTargetStage(IGameContext gameContext, IStateMachineLayer<IRangedContext> parent) : base(gameContext, parent)
        {
            ToFire = new StageBinding(this);
        }

        public override void Enter(IRangedContext context)
        {
            IReadOnlyList<IUnit> potentialTargetUnits = context.AvailableTargetUnits;

            GameContext.GetHandler<IChooseRangedTargetHandler>().Handle(potentialTargetUnits, (unit) => OnChoseRangedTarget(context, unit));
        }

        private void OnChoseRangedTarget(IRangedContext context, IUnit targetUnit)
        {
            context.ChooseTargetUnit(targetUnit);
            GameContext.Log($"Chose target unit: {targetUnit.Name}.");

            ToFire.Activate(context);
        }
    }

    public interface IChooseRangedTargetHandler// : IExitOnlyHandler<IRangedContext>
    {
        public void Handle(IReadOnlyList<IUnit> potentialTargetUnits, Action<IUnit> onChoseUnit);
    }
}