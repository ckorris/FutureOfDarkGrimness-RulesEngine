using FDG.Data;
using System;
using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// The order wounds actually reach a defending unit's models, and the packet arithmetic that
    /// depends on it (#400, #401). Lives next to <see cref="AssignWoundsResults"/> because that is the
    /// thing it mirrors: the order is emergent there from three separate rules, and any consumer that
    /// needs to predict the outcome has to reproduce it. Shared by <c>AssignWoundsStage</c> (the real
    /// resolution) and the Tactician's <c>CombatMath</c> (its valuation estimate), which each carried a
    /// private hand-copy before #400.
    /// </summary>
    public static class WoundAllocation
    {
        /// <summary>
        /// The living models of <paramref name="defender"/> in the order wounds will be poured into
        /// them: already-wounded non-heroes first (#023, the mandatory pre-assignment), then whole
        /// non-heroes in unit-list order, then a joined hero (#006, "heroes are assigned wounds last,
        /// even if already wounded"). Within a model, #024 forces it to be finished before the next is
        /// started, so this list - not the raw model list - is the sequence a wound pool drains along.
        ///
        /// The defender's sub-choice between several already-wounded models is NOT modelled - they fill
        /// in unit-list order, matching <see cref="AssignWoundsResults"/>' own deferred note.
        /// </summary>
        public static List<IModel> Order(IUnit defender) => Order(defender.Models, defender.JoinedHeroModelId);

        /// <summary>
        /// <see cref="Order(IUnit)"/> over an explicit model list - the Tactician prices a hypothetical
        /// survivor set, not the unit as it stands. Eager and single-pass (#191 search perf pass): the
        /// lazy version enumerated the living list three times through LINQ iterators and grew two lists
        /// from empty, per estimate, per candidate.
        /// </summary>
        public static List<IModel> Order(IReadOnlyList<IModel> models, ModelID? heroId)
        {
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
        /// The one place a packet meets a model (#401). Commits <paramref name="packet"/> - or the rest of
        /// it, when <paramref name="alreadyPoured"/> of an unconfined one has gone before - against a model
        /// with <paramref name="capacity"/> wounds left. A confined packet lands <c>weight x min(wounds,
        /// capacity)</c>, loses the remainder, and is always consumed; an unconfined packet pours what
        /// fits and is consumed only once nothing of it remains. <see cref="AssignWoundsResults.TryAddWounds"/>
        /// and <see cref="Simulate"/> both come through here, so the interactive and the predicted
        /// outcomes cannot disagree on the arithmetic.
        /// </summary>
        public static (float Landed, float Lost, bool Consumed) Commit(WoundPacket packet, float alreadyPoured, float capacity)
        {
            if (packet.Confined)
            {
                float landed = packet.Weight * MathF.Min(packet.Wounds, capacity);
                float lost = packet.Weight * MathF.Max(0f, packet.Wounds - capacity);
                return (landed, lost, true);
            }

            float remaining = packet.Wounds - alreadyPoured;
            float poured = MathF.Min(capacity, remaining);
            bool consumed = remaining - poured <= AssignWoundsResults.WoundEpsilon;
            return (poured, 0f, consumed);
        }

        /// <summary>
        /// What <see cref="AssignWoundsResults.AutoFill"/> would land if <paramref name="packets"/> were
        /// poured along <paramref name="orderedModels"/> (see <see cref="Order(IUnit)"/>): each model is
        /// filled until it is dead or the packets run out before the next is started, and whatever the
        /// last model cannot absorb - or a confined packet's excess - is <paramref name="lost"/>. The
        /// predictive twin of the interactive object, for callers that price a volley without building
        /// one; pinned equal to it by <c>WoundPacketAssignmentTests</c>.
        /// </summary>
        public static float Simulate(IReadOnlyList<WoundPacket> packets, IReadOnlyList<IModel> orderedModels, out float lost)
        {
            float landedTotal = 0f;
            lost = 0f;
            int next = 0;
            float headPoured = 0f;

            foreach (IModel model in orderedModels)
            {
                float capacity = model.TotalWounds - model.WoundsDealt;
                while (capacity > AssignWoundsResults.WoundEpsilon && next < packets.Count)
                {
                    (float landed, float packetLost, bool consumed) = Commit(packets[next], headPoured, capacity);
                    landedTotal += landed;
                    lost += packetLost;
                    capacity -= landed;
                    if (consumed) { next++; headPoured = 0f; }
                    else headPoured += landed;
                }
                if (next >= packets.Count) break;
            }

            for (; next < packets.Count; next++)
            {
                lost += packets[next].WeightedWounds - headPoured;
                headPoured = 0f;
            }
            return landedTotal;
        }

        /// <summary>
        /// Deadly's no-carry-over confinement as a scalar. The attack landed <paramref name="clumpCount"/>
        /// failed saves; under Deadly(X) each is a clump of <paramref name="multiplier"/> wounds confined
        /// to one model. Returns the effective wound total along <see cref="Order(IUnit)"/>. #401 replaces
        /// this with real packets in the stage; kept during the transition for its remaining caller.
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
