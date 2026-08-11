
using FDG.Utilities;

namespace FDG.Stages
{

    public class DetermineCanKeepShootingStage : StageBase<ICombatActionContext>
    {
        public StageBinding ReturnToChooseWeapon;
        public StageBinding ToFinishShooting;

        public DetermineCanKeepShootingStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ReturnToChooseWeapon = new StageBinding(this);
            ToFinishShooting = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            // #371: under Declare First the pool empties at DECLARATION time, long before the last volley
            // is rolled - so "nothing left to choose" is only the end of the action once the declaration
            // queue has drained too. ChooseRangedAttackStage fires the next one without asking.
            if (context.AvailableWeapons.Count == 0 && !context.HasPendingAttack)
            {
                GameContext.Log("Has fired all weapons.");
                await ToFinishShooting.Activate(context);
                return;
            }

            /*
            if(context.DefendingUnit == null)
            {
                throw new NullReferenceException($"{nameof(context.DefendingUnit)} was null when entering {nameof(DetermineCanKeepShootingStage)}.");
            }

            if (context.DefendingUnit.RemainingWounds() <= 0)
            {
                GameContext.Log("Has killed all target units.");
                await ToFinishShooting.Activate(context);
                return;
            }
            */

            //We've still got weapons to shoot, and baddies to shoot at. 
            await ReturnToChooseWeapon.Activate(context);
        }
    }
}