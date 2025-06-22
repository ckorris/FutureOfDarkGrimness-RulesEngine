
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{
    public class ChooseRangedTargetStage : StageBase<ICombatActionContext>
    {
        public const string CHOOSE_RANGED_TARGET_TO_FIRE_TRANSITION =
            "ChooseRangedTargetToFire";

        public StageBinding OnChoseTarget;
        public StageBinding BackToChooseAction;

        public ChooseRangedTargetStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnChoseTarget = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered Choose Ranged Target.");

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID;

            //TODO: Use player team instead of ID, to prevent attacking allies.


            void ChooseDefender(IUnit defender)
            {
                //Set the defender on the context.
                GameContext.Log($"Chose {defender.Name} as defender.");
                context.BeginNewAttack(defender);
                OnChoseTarget.Activate(context);
            }

            foreach (IArmy army in GameContext.TableState.Armies.Objects
                .Where(a => a.IsNotOwnedBy(attackingPlayer)))
            {
                foreach (IUnit unit in army.Units)
                {
                    //TODO: Make sure at least one model has a weapon that can fire. For now, list them all.
                    ActionChoice choice = new ActionChoice(() => ChooseDefender(unit), unit.Name, true, null);
                    choices.Add(choice);
                }
            }

            throw new NotImplementedException();
            /*
            GameContext.GetHandler<IChooseRangedTargetHandler>()
                .Handle(context, choices, () => BackToChooseAction.Activate(context));
            */
        }

        private void OnChoseRangedTarget(ICombatActionContext context, IUnit targetUnit)
        {
            context.BeginNewAttack(targetUnit);
            GameContext.Log($"Chose target unit: {targetUnit.Name}.");

            OnChoseTarget.Activate(context);
        }
    }

    public interface IChooseRangedTargetHandler// : IExitOnlyHandler<IRangedContext>
    {
        public void Handle(ICombatActionContext context, List<ActionChoice> actionChoices, Action onCancel);
    }
}