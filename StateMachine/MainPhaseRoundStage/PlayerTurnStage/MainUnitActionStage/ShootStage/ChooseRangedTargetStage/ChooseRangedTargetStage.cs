
using System;
using System.Collections.Generic;

namespace FDG.StateMachine
{

    public class ChooseRangedTargetStage : StateBase<IRangedContext>
    {
        public const string CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION =
            "ChooseRangedTargetToFire";

        public ChooseRangedTargetStage(StateMachine stateMachine, IRangedContext context, StateBase parentState = null)
            : base(stateMachine, context, parentState)
        {
        }

        public override void Enter()
        {
            base.Enter();

            IReadOnlyList<IUnit> potentialTargetUnits = Context.AvailableTargetUnits;

            Context.ChooseRangedTargetHandler.Handle(potentialTargetUnits, OnChoseRangedTarget);
        }

        private void OnChoseRangedTarget(IUnit targetUnit)
        {
            Context.ChooseTargetUnit(targetUnit);
            Context.Log($"Chose target unit: {targetUnit.Name}.");

            SignalEvent(CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION);
        }
    }

    public interface IChooseRangedTargetHandler// : IExitOnlyHandler<IRangedContext>
    {
        public void Handle(IReadOnlyList<IUnit> potentialTargetUnits, Action<IUnit> onChoseUnit);
    }
}