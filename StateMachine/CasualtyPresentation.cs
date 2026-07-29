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
        /// Deal <paramref name="wounds"/> wounds owed by the UNIT as a whole: fill living models
        /// front-to-back, each absorbing up to its own remaining wounds, present every casualty through
        /// <see cref="ApplyAndPresent"/>, and fire the destruction seam if the unit dies of it. Surplus
        /// beyond the unit's total capacity is dropped rather than wrapping.
        ///
        /// <para>
        /// This is the shape a rule takes when the unit owes a POOL of wounds, as opposed to the
        /// dangerous-terrain shape where every model rolls its own die and wounds land where they fell.
        /// Callers so far: No Retreat's failed-morale conversion (#197 P7) and Hazardous's overheat
        /// (#197). The <paramref name="wounds"/> total stays FRACTIONAL — it comes off a histogram, and a
        /// roll-derived wound total is never int-locked (only dice-POOL sizes are floored).
        /// </para>
        ///
        /// <para>
        /// By construction this bypasses saves and the wound-ignore pipeline: self-inflicted damage is
        /// not an attack, so there is no save to roll and no Regeneration read — the same treatment
        /// dangerous terrain already gets. Owner-ruled 2026-07-29 for Hazardous, whose corpus text does
        /// not say "can't be ignored", on consistency with every other self-harm path in the engine.
        /// </para>
        /// </summary>
        public static async Task ApplyUnitWounds(IGameContext gameContext, IUnit unit, float wounds)
        {
            if (wounds <= 0f) return;

            bool wasAlive = unit.GetIsAlive();
            await ApplyAndPresent(gameContext, SpreadWounds(unit, wounds), unit.ID, unit.Name);

            // Self-inflicted, but a destruction like any other: OwnerDestroyed marks on other units have
            // to clear and a wrecked Transport has to spill its cargo. Killer-less, so no attacker is
            // credited.
            if (wasAlive && !unit.GetIsAlive())
            {
                await UnitDestructionNotifier.NotifyUnitDestroyed(gameContext, unit, killer: null);
            }
        }

        /// <summary>
        /// Fills living models front-to-back, each absorbing up to its own remaining wounds - the way
        /// damage removes models, not the per-model spread a dangerous-terrain test uses (there every
        /// model rolls its own die; here one pool is owed by the unit).
        /// </summary>
        private static List<PendingModelWound> SpreadWounds(IUnit unit, float wounds)
        {
            var pending = new List<PendingModelWound>();
            float remaining = wounds;
            foreach (IModel model in unit.Models)
            {
                if (remaining <= 0f) break;
                if (!model.GetIsAlive()) continue;

                float capacity = model.TotalWounds - model.WoundsDealt;
                float take = Math.Min(capacity, remaining);
                if (take <= 0f) continue;

                pending.Add(new PendingModelWound(model, take));
                remaining -= take;
            }
            return pending;
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
