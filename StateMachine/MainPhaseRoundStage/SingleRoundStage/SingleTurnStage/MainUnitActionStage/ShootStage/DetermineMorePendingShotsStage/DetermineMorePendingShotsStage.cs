namespace FDG.Stages
{
    /// <summary>
    /// #157 — after each FireStage run, loops back to fire the next queued attack while any remain.
    /// Normally a shoot fires one queued batch and this falls straight through; a Takedown-split
    /// weapon (see <see cref="ICombatActionContext.SplitPendingAttackIntoSingleShots"/>) queues one
    /// single-shot attack per copy, so each shot re-enters FireStage and picks its own target model.
    /// If the burst's target unit died mid-way, the leftover shots fizzle (they were declared at that
    /// unit; there is nothing left to aim at).
    ///
    /// Exiting here means this weapon is spent, not that the activation is over — the shoot stage still
    /// asks whether another weapon can fire. Morale is resolved once, after the last one.
    /// </summary>
    public class DetermineMorePendingShotsStage : StageBase<ICombatActionContext>
    {
        public StageBinding FireNextShot;
        public StageBinding OnVolleyComplete;

        public DetermineMorePendingShotsStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent)
            : base(gameContext, parent)
        {
            FireNextShot = new StageBinding(this);
            OnVolleyComplete = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.HasPendingAttack)
            {
                bool defenderAlive = context.DefendingUnit.GetValue().Models.Any(model => model.GetIsAlive());
                if (!defenderAlive)
                {
                    context.ClearPendingAttacks();
                    GameContext.Log("Target destroyed - the burst's remaining shots are discarded.");
                    await OnVolleyComplete.Activate(context);
                    return;
                }

                await FireNextShot.Activate(context);
                return;
            }

            await OnVolleyComplete.Activate(context);
        }
    }
}
