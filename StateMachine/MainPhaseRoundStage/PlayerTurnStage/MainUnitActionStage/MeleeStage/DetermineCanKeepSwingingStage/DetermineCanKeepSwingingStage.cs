
namespace FDG.Stages
{
    internal class DetermineCanKeepSwingingStage : StageBase<IMeleeContext>
    {

        public StageBinding ReturnToChooseWeapon;
        public StageBinding OnOutOfWeapons;
        public StageBinding OnDefenderKilled;

        //private readonly IMeleeContext _meleeContext;

        public DetermineCanKeepSwingingStage(IGameContext gameContext, IStateMachineLayer<IMeleeContext> parent) : base(gameContext, parent)
        {
            ReturnToChooseWeapon = new StageBinding(this);
            OnOutOfWeapons = new StageBinding(this);
            OnDefenderKilled = new StageBinding(this);
        }

        public override void Enter(IMeleeContext context)
        {
            int remainingWeaponCount = context.AvailableWeapons.Count;

            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if (remainingWeaponCount == 0)
            {
                GameContext.Log("Has swung with all melee weapons.");
                OnOutOfWeapons.Activate(context);
                return;
            }

            if (context.DefendingUnit.RemainingWounds <= 0)
            {
                GameContext.Log("Has killed all target units.");
                OnDefenderKilled.Activate(context);
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

            ReturnToChooseWeapon.Activate(context);
        }
    }
}
