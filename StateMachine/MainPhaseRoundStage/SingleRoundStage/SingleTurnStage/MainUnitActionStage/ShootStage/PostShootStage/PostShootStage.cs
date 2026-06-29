using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// Convergence stage at the end of a shoot action: both shoot exits (fired all weapons, or no
    /// further valid shots) route here before <c>ShootStage.OnFinishedShooting</c>. Fires the
    /// <see cref="EHookID.Shooting_OnPostShoot"/> hook once for the acting unit and enacts any
    /// resulting triggered move — the seam that makes the dormant "after shooting" hook live so
    /// Hit & Run / Harassing can reposition after firing.
    ///
    /// The post-shoot move is a passive <see cref="HookEntry"/> producing an
    /// <see cref="Effect.TriggeredMove"/> (optional, so a unit may decline), enacted through the
    /// movement subsystem via <see cref="OperationExecutor"/> — the same chain
    /// <see cref="DeployUnitStage"/> uses for Vanguard's deploy-time reposition.
    /// </summary>
    public class PostShootStage : StageBase<ICombatActionContext>
    {
        public StageBinding ToFinished;

        public PostShootStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit unit = context.AttackingUnit.GetValue();

            // One per-action evaluation of the post-shoot hook for the shooter (seat Actor): a
            // Hit & Run / Harassing HookEntry yields an InvokeTriggeredMove the executor runs through
            // the movement subsystem. Units without such a rule produce no operations — no-op.
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                new PostShootActionContext(unit), (unit, ERuleSeat.Actor));

            // OperationExecutor runs only the imperative ExecutableOperations (the triggered move);
            // any token ops a future post-shoot rule emits are applied separately, mirroring DeployUnitStage.
            OperationApplier.ApplyTokenOperations(operations);
            await OperationExecutor.Execute(operations, new GameOperationServices(GameContext));

            await ToFinished.Activate(context);
        }
    }
}
