using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// One parcel of wounds on its way to a defending unit (#401). The wound pipeline used to carry a
    /// single scalar total, which cannot express Deadly: "assign each wound to one model, multiply it by
    /// X, and these wounds don't carry over to other models if the original target is killed". A packet
    /// can.
    /// <list type="bullet">
    ///   <item>An <b>unconfined</b> packet is the ordinary pool: it drains model by model (each one
    ///   finished before the next is started, #024) and nothing is ever lost while a model can still
    ///   take a wound. Every plain volley is exactly one of these, so the old behaviour is preserved by
    ///   construction.</item>
    ///   <item>A <b>confined</b> packet is a Deadly clump: all of it goes to ONE model, and whatever
    ///   that model cannot absorb is lost - it never reaches the next model.</item>
    /// </list>
    /// <see cref="Weight"/> is the packet's probability mass, 1 for a whole packet. Under the
    /// probabilistic roller a failed-save count is fractional (2.34 clumps), which reads as two sure
    /// clumps plus a 34%-likely one: what lands is <c>Weight x min(Wounds, capacity)</c>, so a
    /// 0.34-weight clump of 3 on a 1-wound model lands 0.34 - not the 1.0 that a bare 1.02-wound packet
    /// would. Only confined packets carry a weight below 1.
    ///
    /// <para><see cref="Wounds"/> is what the packet delivers once the stage has rolled Regeneration
    /// for it - the dice are rolled per packet, BEFORE the capacity cap, which is what lets a Deadly(3)
    /// clump on a 1-wound Regeneration model get its three shrug rolls.</para>
    /// </summary>
    public sealed class WoundPacket
    {
        public float Wounds { get; }

        public bool Confined { get; }

        public float Weight { get; }

        /// <summary>The wounds this packet carried before Regeneration was rolled for it - a Deadly(3)
        /// clump's 3, whatever is left in <see cref="Wounds"/>. What a dialog needs to say "3 rolled,
        /// 2 ignored".</summary>
        public float OriginalWounds { get; }

        [JsonConstructor]
        public WoundPacket(float wounds, bool confined, float weight, float originalWounds)
        {
            Wounds = wounds;
            Confined = confined;
            Weight = weight;
            OriginalWounds = originalWounds;
        }

        private WoundPacket(float wounds, bool confined, float weight) : this(wounds, confined, weight, wounds)
        {
        }

        /// <summary>The ordinary pool: <paramref name="wounds"/> that drain across the unit, nothing lost
        /// while a model can still take a wound.</summary>
        public static WoundPacket Unconfined(float wounds) => new WoundPacket(wounds, confined: false, weight: 1f);

        /// <summary>A Deadly clump: <paramref name="wounds"/> that all land on one model, the excess lost.
        /// <paramref name="weight"/> below 1 is a fractional clump under the probabilistic roller.</summary>
        public static WoundPacket Clump(float wounds, float weight = 1f) => new WoundPacket(wounds, confined: true, weight);

        /// <summary>The same packet carrying <paramref name="wounds"/> instead - what Regeneration leaves of it.</summary>
        public WoundPacket WithWounds(float wounds) => new WoundPacket(wounds, Confined, Weight, OriginalWounds);

        /// <summary>What Regeneration took off this packet.</summary>
        public float Ignored => OriginalWounds - Wounds;

        /// <summary>The wounds this packet carries, weighted - the most it could ever land.</summary>
        public float WeightedWounds => Wounds * Weight;

        public override string ToString() =>
            $"{(Confined ? "clump" : "pool")} of {Wounds:0.##}{(Weight < 1f ? $" x{Weight:0.##}" : "")}";
    }
}
