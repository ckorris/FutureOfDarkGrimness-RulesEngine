using SharpFont.PostScript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{
    public class ChooseMeleeDefenderStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnDefenderChosen;
        public StageBinding BackToChooseAction;

        public ChooseMeleeDefenderStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            OnDefenderChosen = new StageBinding(this);
            BackToChooseAction = new StageBinding(this);
        }

        public override void Enter(ICombatActionContext context)
        {
            GameContext.Log("Entered Choose Melee Defender.");

            List<ActionChoice> choices = new List<ActionChoice>();

            PlayerID attackingPlayer = context.AttackingUnit.PlayerID;
            
            //TODO: Use player team instead of ID, to prevent attacking allies.

            void ChooseDefender(IUnit defender)
            {
                //Set the defender on the context.
                GameContext.Log($"Chose {defender.Name} as defender.");
                context.BeginNewAttack(defender);
                OnDefenderChosen.Activate(context);
            }

            foreach(IArmy army in GameContext.TableState.ArmyState.PlayerArmies
                .Where(kvp => kvp.Key != attackingPlayer)
                .Select(kvp => kvp.Value))
            {
                foreach(IUnit unit in army.Units)
                {
                    //TODO: Judge distance from attacking unit. For now, list them all.
                    ActionChoice choice = new ActionChoice(() => ChooseDefender(unit), unit.Name, true, null);
                    choices.Add(choice);
                }
            }

            GameContext.GetHandler<IChooseMeleeDefenderHandler>()
                .Handle(context, choices, () => BackToChooseAction.Activate(context));
        }
    }

    public interface IChooseMeleeDefenderHandler
    {
        public void Handle(ICombatActionContext context, List<ActionChoice> actionChoices, Action onCancel);
    }
}
