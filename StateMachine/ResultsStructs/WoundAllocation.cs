using FDG.Data;
using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// The order wounds actually reach a defending unit's models, and the wound-multiplier (Deadly)
    /// confinement that depends on it. Lives next to <see cref="AssignWoundsResults"/> because that is
    /// the thing it mirrors: the order is emergent there from three separate rules, and any consumer
    /// that needs to predict the outcome has to reproduce it. Shared by <c>AssignWoundsStage</c> (the
    /// real resolution) and the Tactician's <c>CombatMath</c> (its valuation estimate), which each
    /// carried a private hand-copy before #400.
    /// </summary>
    public static class WoundAllocation
    {
        /// <summary>
        /// The living models of <paramref name="defender"/> in the order wounds will be poured into
        /// them: already-wounded non-heroes first (#023, the mandatory pre-assignment), then whole
        /// non-heroes in unit-list order, then a joined hero (#006, "heroes are assigned wounds last,
        /// even if already wounded"). Within a model, #024 forces it to be finished before the next is
        /// started, so this list — not the raw model list — is the sequence a wound pool drains along.
        ///
        /// Eager and single-pass (#191 search perf pass): the lazy version enumerated the living list
        /// three times through LINQ iterators and grew two lists from empty, per estimate, per candidate.
        ///
        /// The defender's sub-choice between several already-wounded models is NOT modelled — they fill
        /// in unit-list order, matching <see cref="AssignWoundsResults"/>' own deferred note.
        /// </summary>
        public static List<IModel> Order(IUnit defender)
        {
            ModelID? heroId = defender.JoinedHeroModelId;
            List<IModel> models = defender.Models;
            var order = new List<IModel>(models.Count);
            IModel? hero = null;

            for (int i = 0; i < models.Count; i++)
            {
                IModel model = models[i];
                if (!model.GetIsAlive()) continue;
                if (heroId.HasValue && model.ID.Equals(heroId.Value)) { hero ??= model; continue; }
                if (model.WoundsDealt > 0f) order.Add(model);
            }
            for (int i = 0; i < models.Count; i++)
            {
                IModel model = models[i];
                if (!model.GetIsAlive()) continue;
                if (heroId.HasValue && model.ID.Equals(heroId.Value)) continue;
                if (model.WoundsDealt <= 0f) order.Add(model);
            }
            if (hero != null) order.Add(hero);
            return order;
        }

        /// <summary>
        /// Deadly's no-carry-over confinement. The attack landed <paramref name="clumpCount"/> failed
        /// saves; under Deadly(X) each is a clump of <paramref name="multiplier"/> wounds confined to one
        /// model, with any overkill on that model lost rather than carrying to the next. Assigns whole
        /// clumps until each model is dead (ceil(capacity / X) clumps) and sums the wounds that actually
        /// land (a clump on a model deals min(X, that model's remaining), so a 1-wound model absorbs only
        /// 1 of the X). Returns the effective wound total, which replaces the naive total*X.
        ///
        /// #400: walks <see cref="Order"/>, NOT the raw model list. The two differ whenever the squad is
        /// already damaged, and the caller then spends the returned total along <see cref="Order"/> — so
        /// walking the raw list computed the cap against one model and spent it on another, spilling a
        /// clump's discarded overkill onto a fresh model. Against fresh uniform squads the orders
        /// coincide, which is why the bug survived #028's tests.
        /// </summary>
        public static float ConfineToClumps(float clumpCount, int multiplier, IUnit defender)
        {
            float effective = 0f;
            float remainingClumps = clumpCount;

            foreach (IModel model in Order(defender))
            {
                if (remainingClumps <= 0f) break;

                float capacity = model.TotalWounds - model.WoundsDealt;
                if (capacity <= 0f) continue;

                float clumpsToKill = MathF.Ceiling(capacity / multiplier);
                float used = MathF.Min(remainingClumps, clumpsToKill);
                effective += MathF.Min(used * multiplier, capacity);
                remainingClumps -= used;
            }

            return effective;
        }
    }
}
