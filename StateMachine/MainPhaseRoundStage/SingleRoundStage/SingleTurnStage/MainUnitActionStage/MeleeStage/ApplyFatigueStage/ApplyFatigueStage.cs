
namespace FDG.Stages
{
    public class ApplyFatigueStage : StageBase<ICombatActionContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public StageBinding OnFatigueApplied;

        public ApplyFatigueStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnFatigueApplied = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // #020 Fatigue: units that fought in this melee become Fatigued for the rest of the round,
            // after which they hit only on unmodified 6s in further melee. Applied here, at the end of
            // the melee, so the swings just resolved used full Quality. Up to three units can need it,
            // and FatigueUtilities.ApplyFatigued is idempotent so the overlaps are harmless:
            //   - AttackingUnit:  the unit that swung first (the charger, or a Counter unit that swapped in).
            //   - ChargingUnit:   the unit that charged — fatigued for charging even if a Counter unit
            //                     denied it a swing (recoverable after SwapCombatRoles via this capture).
            //   - DefendingUnit:  fatigued only if it actually struck back.
            IUnit swinger = context.AttackingUnit.GetValue();
            FatigueUtilities.ApplyFatigued(swinger);
            GameContext.Log($"{swinger.Name} is Fatigued.");

            if (context.ChargingUnit != null)
            {
                FatigueUtilities.ApplyFatigued(context.ChargingUnit.GetValue());
            }

            if (context.DefenderStruckBack)
            {
                IUnit striker = context.DefendingUnit.GetValue();
                FatigueUtilities.ApplyFatigued(striker);
                GameContext.Log($"{striker.Name} struck back and is Fatigued.");
            }

            await OnFatigueApplied.Activate(context);
        }
    }
}
