using FDG.Data;
using FDG.Presentation.Beats;

namespace FDG.Stages
{
    /// <summary>
    /// One wound a batched test owes a model, computed but NOT yet applied. Batched tests (dangerous
    /// terrain, transport spillout) must roll all their dice at once - a single N-die roll is what lets
    /// the probabilistic roller yield the expected number of 1s - but they must not APPLY the results
    /// early: see <see cref="CasualtyPresentation"/> for why.
    /// </summary>
    public readonly record struct PendingModelWound(IModel Model, float Wounds);

    /// <summary>
    /// The one way a batched test turns pending wounds into dead models on screen.
    ///
    /// <para>
    /// The front-end hides any model that is dead in authoritative state but has no death beat
    /// registered (<c>PresentationPlayer.GetModelDrawState</c>) - it has no way to know an animation is
    /// still coming. The death override is registered when the beat is ENQUEUED, so the only safe shape
    /// is to deal a model's wound and present its beat in the same instant, with nothing awaited in
    /// between. <c>ApplyWoundsStage</c> has always done this; the batched tests did not, which is what
    /// made a dangerous-terrain casualty vanish outright (no beat was ever emitted) and a spillout
    /// casualty blink out at placement and then pop back to play its death animation seconds later,
    /// once the dice beat had finished and the death beat finally enqueued.
    /// </para>
    ///
    /// <para>
    /// So: roll the batch, present the dice, THEN call this. Every model dies exactly once - the
    /// alive-before guard means a model already dead (killed earlier in the same resolution) is never
    /// wounded again or animated twice, so no death can be double-played.
    /// </para>
    /// </summary>
    public static class CasualtyPresentation
    {
        /// <summary>
        /// Apply each pending wound and present its casualty beat immediately, in order. Models already
        /// dead are skipped entirely (no wound, no beat). Mirrors <c>ApplyWoundsStage</c>'s #232 casualty
        /// cascade: every casualty except the last overlaps the next, so a multi-kill batch reads as
        /// rapid fire rather than a queue. No-op when nothing is pending.
        /// </summary>
        public static async Task ApplyAndPresent(IGameContext gameContext,
            IReadOnlyList<PendingModelWound> pending, UnitID unit, string unitName)
        {
            // The last entry that will actually emit a beat - everything before it overlaps (#232).
            int lastCasualtyIndex = -1;
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].Wounds > 0f && pending[i].Model.GetIsAlive()) lastCasualtyIndex = i;
            }

            for (int i = 0; i < pending.Count; i++)
            {
                PendingModelWound entry = pending[i];

                // Alive-before guard: a model killed earlier in this same batch (or by anything else
                // between the roll and here) has already had its death animated. Never twice.
                if (entry.Wounds <= 0f || !entry.Model.GetIsAlive()) continue;

                entry.Model.DealWounds(entry.Wounds);

                bool overlap = i != lastCasualtyIndex;

                // Position is read after the wound lands - a death animates where the model fell, which
                // for a batched test is wherever it finished being placed / moved.
                if (entry.Model.GetIsDead())
                {
                    await gameContext.Presenter.Present(
                        new ModelDiedBeat(entry.Model.ID, unit, unitName, entry.Model.Position, overlap));
                }
                else
                {
                    await gameContext.Presenter.Present(
                        new ModelWoundedBeat(entry.Model.ID, entry.Model.Position, overlap));
                }
            }
        }

        /// <summary>
        /// Apply the batch with no presentation at all - for out-of-band callers (tests, headless
        /// state-only paths) that want the state change without a presenter. Same alive-before guard.
        /// </summary>
        public static void ApplyOnly(IReadOnlyList<PendingModelWound> pending)
        {
            foreach (PendingModelWound entry in pending)
            {
                if (entry.Wounds <= 0f || !entry.Model.GetIsAlive()) continue;
                entry.Model.DealWounds(entry.Wounds);
            }
        }
    }
}
