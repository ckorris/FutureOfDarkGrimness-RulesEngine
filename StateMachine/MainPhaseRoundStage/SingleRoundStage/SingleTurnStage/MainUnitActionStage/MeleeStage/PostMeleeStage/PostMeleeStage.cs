using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// Convergence stage at the end of a resolved melee (after consolidation): fires the
    /// <see cref="EHookID.Melee_OnPostMelee"/> hook for the charged/attacked unit and enacts any
    /// resulting triggered move — the melee twin of <see cref="PostShootStage"/>, lighting the dormant
    /// "after melee" hook so Harassing can disengage after being attacked.
    ///
    /// Only the CHARGED unit is offered the move (Harassing is "after being attacked in melee", per the
    /// <see cref="EHookID.Melee_OnPostMelee"/> doc), identified via the immutable
    /// <see cref="ICombatActionContext.ChargingUnit"/> so a Counter role-swap doesn't confuse it. Guarded
    /// on <see cref="IUnitExtensions.GetIsAlive"/>: the charged unit can be wiped out by the melee, and a
    /// destroyed unit has nothing to move. The <c>BackToChooseAction</c> exit (no melee happened) bypasses
    /// consolidation and so never reaches this stage.
    /// </summary>
    public class PostMeleeStage : StageBase<ICombatActionContext>
    {
        public StageBinding ToFinished;

        public PostMeleeStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacked = ResolveAttackedUnit(context);

            if (attacked.GetIsAlive())
            {
                // Per-action evaluation of the post-melee hook for the attacked unit (seat Actor): a
                // Harassing HookEntry yields an optional InvokeTriggeredMove the executor runs through the
                // movement subsystem. A unit without such a rule produces no operations — no-op.
                IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                    new PostMeleeActionContext(attacked), RuleParticipant.Actor(attacked));

                // Gate enacts the move (if the unit has a post-melee rule and hasn't already moved this
                // round) and enforces the family's once-per-round budget — shared with the shooting seam.
                await PostCombatMoveGate.OfferIfAvailable(GameContext, attacked, operations);
            }

            await ToFinished.Activate(context);
        }

        // The charged unit is the melee participant that is NOT the charger. ChargingUnit is captured at
        // context creation and never reassigned by SwapCombatRoles, so it stays the charger even after a
        // Counter swap flips AttackingUnit/DefendingUnit — keying off it picks the charged unit either way.
        private static IUnit ResolveAttackedUnit(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit? charger = context.ChargingUnit?.GetValue();

            if (charger != null && attacker.ID.Equals(charger.ID))
            {
                return context.DefendingUnit.GetValue();
            }

            // Roles swapped (Counter) or no charger recorded: the current attacker is the charged unit.
            return attacker;
        }
    }
}
