namespace FDG.Stages
{
    /// <summary>
    /// #034 — the loop-decision between damage targets: after one target's damage resolves, continue to the
    /// next chosen target or finish the cast. The spell-damage analog of <c>DetermineCanKeepShootingStage</c>;
    /// it lets <see cref="CastSpellStage"/> drive <see cref="ResolveSpellDamageStage"/> once per selected
    /// unit (a fresh metadata each time) instead of hitting only the first target.
    /// </summary>
    public class DetermineMoreSpellTargetsStage : StageBase<SpellDamageRunContext>
    {
        public StageBinding ResolveNextTarget;
        public StageBinding ToFinished;

        public DetermineMoreSpellTargetsStage(IGameContext gameContext, IStateMachineLayer<SpellDamageRunContext> parent)
            : base(gameContext, parent)
        {
            ResolveNextTarget = new StageBinding(this);
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(SpellDamageRunContext context)
        {
            if (context.HasMoreTargets)
            {
                await ResolveNextTarget.Activate(context);
                return;
            }

            await ToFinished.Activate(context);
        }
    }
}
