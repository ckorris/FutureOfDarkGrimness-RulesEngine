using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Convergence stage at the end of a resolved melee (after consolidation): fires the
    /// <see cref="EHookID.Melee_OnPostMelee"/> hook for each combatant and enacts any resulting
    /// triggered move — the melee twin of <see cref="PostShootStage"/>, lighting the dormant "after
    /// melee" hook for the Harassing family.
    ///
    /// #391: BOTH combatants get the offer ("may move up to 3\" ... after being in melee" is
    /// role-neutral - pre-#391 only the charged unit was offered, so a unit that CHARGED with Harassing
    /// never got its move). The active player's unit (the charger) resolves first, per the core
    /// simultaneous-trigger convention; the charged unit second - identified via the immutable
    /// <see cref="ICombatActionContext.ChargingUnit"/> so a Counter role-swap doesn't confuse it. Each
    /// unit pays its own <c>PostCombatMoveUsed</c> budget, and the gate refuses a Shaken unit (#391) -
    /// which is how a unit Shaken by losing THIS melee is denied the disengage. Guarded on
    /// <see cref="IUnitExtensions.GetIsAlive"/>: either combatant can be wiped out by the melee, and a
    /// destroyed unit has nothing to move. The <c>BackToChooseAction</c> exit (no melee happened)
    /// bypasses consolidation and so never reaches this stage.
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
            // #391: both combatants, charger first (see the class doc for the ordering rationale).
            foreach (DataBinding<UnitData> combatantBinding in PostMeleeMoveOrder(context))
            {
                IUnit combatant = combatantBinding.GetValue();
                if (!combatant.GetIsAlive()) continue;

                // Per-action evaluation of the post-melee hook for this combatant (seat Actor): a
                // Harassing HookEntry yields an optional InvokeTriggeredMove the executor runs through the
                // movement subsystem. A unit without such a rule produces no operations — no-op.
                IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                    new PostMeleeActionContext(combatant), RuleParticipant.Actor(combatant));

                // Gate enacts the move (if the unit has a post-melee rule, isn't Shaken, and hasn't
                // already moved this round) and enforces the family's once-per-round budget — shared with
                // the shooting seam. #381: a REAL reposition is recorded for
                // RetreatingStrikePostCombatStage (next in the chain), so the move-end strike hook fires
                // on the final positions.
                bool moved = await PostCombatMoveGate.OfferIfAvailable(GameContext, combatant, operations);
                if (moved)
                {
                    context.PostCombatMovers.Add(combatantBinding);
                }
            }

            // #197 P12: the per-melee token sweep. Melee_OnPostMelee was a hook nothing swept, so a
            // CustomHook(Melee_OnPostMelee) token could be placed but never cleared. Both combatants are
            // swept, not just the attacked unit: a per-melee gate belongs to whichever unit set it, and
            // the charger and the striker-back both can. Runs unconditionally (no GetIsAlive guard) - a
            // dead unit's stale gate is harmless, but Reanimation-style restoration means "harmless" is
            // not worth relying on, and clearing costs nothing.
            //
            // The BackToChooseAction exit bypasses this stage, which is correct: no swing happened there,
            // so no gate can have been set.
            List<ITokenContainer> containers = new List<ITokenContainer>();
            foreach (IUnit combatant in new[] { context.AttackingUnit.GetValue(), context.DefendingUnit.GetValue() })
            {
                containers.Add(combatant.Tokens);
                containers.AddRange(combatant.Models.Select(model => model.Tokens));
            }
            new TokenClearService().ClearForHook(EHookID.Melee_OnPostMelee, containers);

            await ToFinished.Activate(context);
        }

        // #391: the offer order - the charger (the active player's unit) first, then the charged unit.
        // ChargingUnit is captured at context creation and never reassigned by SwapCombatRoles, so it
        // stays the charger even after a Counter swap flips AttackingUnit/DefendingUnit — keying off it
        // identifies the roles either way. With no charger recorded (defensive; the melee chain is
        // charge-only today) the current attacker leads.
        private static IEnumerable<DataBinding<UnitData>> PostMeleeMoveOrder(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit? charger = context.ChargingUnit?.GetValue();

            bool attackerIsCharger = charger == null || attacker.ID.Equals(charger.ID);

            yield return attackerIsCharger ? context.AttackingUnit : context.DefendingUnit;
            yield return attackerIsCharger ? context.DefendingUnit : context.AttackingUnit;
        }
    }
}
