
namespace FDG.Stages
{

    public class DetermineCanKeepShootingStage : StageBase<IRangedContext>
    {
        public StageBinding ReturnToChooseWeapon;
        public StageBinding ToFinishShooting;

        public DetermineCanKeepShootingStage(IGameContext gameContext, IStateMachineLayer<IRangedContext> parent) : base(gameContext, parent)
        {
            ReturnToChooseWeapon = new StageBinding(this);
            ToFinishShooting = new StageBinding(this);
        }

        public override void Enter(IRangedContext context)
        {
            //Return to choose weapon again if there are weapons remaining and the target is still alive.
            if (context.AvailableWeapons.Count == 0)
            {
                GameContext.Log("Has fired all weapons.");
                ToFinishShooting.Activate(context);
                return;
            }

            if (context.RangedCombatMetadata.DefendingUnit.RemainingWounds <= 0)
            {
                GameContext.Log("Has killed all target units.");
                ToFinishShooting.Activate(context);
                return;
            }

            //We've still got weapons to shoot, and baddies to shoot at. 
            context.ResetRangedCombatMetadata();
            ReturnToChooseWeapon.Activate(context);
        }
    }
}