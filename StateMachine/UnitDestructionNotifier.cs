using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// The "a unit was just destroyed" seam. Callers invoke <see cref="NotifyUnitDestroyed"/> exactly
    /// once per alive-to-dead transition (guarded by an alive-before/dead-after check at the call site,
    /// since the engine has no unit-removal primitive or death event to subscribe to):
    /// <list type="bullet">
    ///   <item>Cross-unit token cleanup always runs: marks the dead unit placed on other units with
    ///         <see cref="TokenClearTrigger.OwnerDestroyed"/> clear the moment their placer dies.</item>
    ///   <item><see cref="EHookID.Shooting_OnUnitDestroyed"/> fires only when the death has an
    ///         attributable killer (weapon and spell attacks pass the attacker). Morale routs and
    ///         dangerous-terrain deaths pass no killer and skip the hook —
    ///         <see cref="UnitDestroyedContext"/> requires one, and whether a routed unit counts as
    ///         "destroyed by" the melee winner is an open rules question (see SpecialRulesAudit.md).</item>
    /// </list>
    /// </summary>
    public static class UnitDestructionNotifier
    {
        public static async Task NotifyUnitDestroyed(IGameContext gameContext, IUnit dead, IUnit? killer)
        {
            // A destroyed unit's OwnerDestroyed marks die with it, wherever they sit.
            List<ITokenContainer> containers = new List<ITokenContainer>();
            foreach (IUnit unit in gameContext.TableState.Units.Objects)
            {
                containers.Add(unit.Tokens);
                containers.AddRange(unit.Models.Select(model => model.Tokens));
            }
            new TokenClearService().ClearForDestroyedOwner(dead.ID, containers);

            if (killer == null)
            {
                return;
            }

            IReadOnlyList<RuleOperation> ops = gameContext.RuleEvaluator.EvaluateAll(
                new UnitDestroyedContext(DestroyedUnit: dead, KillerUnit: killer),
                (killer, ERuleSeat.Actor, (IWeapon?)null),
                (dead, ERuleSeat.Subject, (IWeapon?)null));
            OperationApplier.ApplyTokenOperations(ops);
            await OperationExecutor.Execute(ops, new GameOperationServices(gameContext));
        }
    }
}
