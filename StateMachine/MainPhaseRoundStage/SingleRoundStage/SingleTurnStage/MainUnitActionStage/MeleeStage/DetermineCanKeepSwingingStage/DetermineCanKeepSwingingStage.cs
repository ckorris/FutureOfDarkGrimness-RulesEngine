
using FDG.Utilities;

namespace FDG.Stages
{
    internal class DetermineCanKeepSwingingStage : StageBase<ICombatActionContext>
    {

        public StageBinding ReturnToChooseWeapon;
        public StageBinding OnOutOfWeapons;
        public StageBinding OnDefenderKilled;

        //private readonly IMeleeContext _meleeContext;

        public DetermineCanKeepSwingingStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ReturnToChooseWeapon = new StageBinding(this);
            OnOutOfWeapons = new StageBinding(this);
            OnDefenderKilled = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            int remainingWeaponCount = context.AvailableWeapons.Count;

            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if (remainingWeaponCount == 0)
            {
                GameContext.Log("Has swung with all melee weapons.");
                await OnOutOfWeapons.Activate(context);
                return;
            }

            if (context.DefendingUnit.RemainingWounds() <= 0)
            {
                GameContext.Log("Has killed all target units.");
                await OnDefenderKilled.Activate(context);
                return;
            }

            //We've still got weapons to shoot, and baddies to shoot at. 
            if (remainingWeaponCount == 1)
            {
                GameContext.Log("Has 1 more weapon left to swing.");
            }
            else
            {
                GameContext.Log($"Has {remainingWeaponCount} more weapons left to swing.");
            }

            await ReturnToChooseWeapon.Activate(context);
        }
    }
}
