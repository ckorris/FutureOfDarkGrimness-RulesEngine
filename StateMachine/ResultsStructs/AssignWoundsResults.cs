using FDG.Data;
using FDG.Utilities;
using Newtonsoft.Json;

namespace FDG
{

    /// <summary>
    /// The defender's wound assignment for one attack: an ordered queue of <see cref="WoundPacket"/>s
    /// (#401) and the models they are being committed to. Every assignment-ordering rule is enforced
    /// HERE, in the one object every resolver (GUI, CLI, both AIs, the network reply) manipulates, so
    /// no front end can produce an illegal allocation: a joined hero is wounded last (#006), an
    /// already-wounded model is finished first (#023), a model mid-fill must be finished before another
    /// is started (#024), and a confined packet - a Deadly clump - lands on exactly one model with its
    /// excess lost rather than carried (#401). <see cref="TryAddWounds"/> commits the NEXT packet to the
    /// chosen model; the caller's whole decision is which model, never how much.
    /// </summary>
    public class AssignWoundsResults
    {
        /// <summary>
        /// #199: wound quantities are float chains (hit x save x regeneration expected values), and the
        /// pool and the per-model capacities are computed by DIFFERENT summation orders/round-trips, so
        /// exact float comparisons intermittently declare a dying model "full" while a ~0.05 residue
        /// remains - and AutoFill threw, faulting the whole game. Anything below this epsilon is
        /// rounding noise, orders of magnitude beneath the smallest meaningful wound fraction (1/216).
        /// </summary>
        public const float WoundEpsilon = 0.0001f;

        // The queue and the cursor into it. _headPoured is how much of the head packet has already
        // been poured when it is unconfined (a confined packet is consumed whole). All serialized so
        // a mid-assignment save/network round-trip resumes exactly where it was.
        [JsonProperty("Packets")] private List<WoundPacket> _packets;
        [JsonProperty("PacketsCommitted")] private int _packetsCommitted;
        [JsonProperty("HeadPoured")] private float _headPoured;
        [JsonProperty("WoundsLostSoFar")] private float _woundsLost;

        // #006: the joined hero's model (if any). The hero is wounded last — ordered last in PendingWounds
        // (so AutoFill spares it) and rejected by TryAddWounds while any rank-and-file model has room.
        // Serialized so the guard survives a mid-assignment save/network round-trip; null for non-hero units.
        [JsonProperty] private ModelID? _heroModelId;

        public float TotalAssignedWounds { get; private set; }

        public List<PendingWounds> PendingWounds;

        [JsonConstructor]
        public AssignWoundsResults(List<WoundPacket> packets, int packetsCommitted, float headPoured,
            float woundsLostSoFar, float totalAssignedWounds, List<PendingWounds> pendingWounds)
        {
            _packets = packets;
            _packetsCommitted = packetsCommitted;
            _headPoured = headPoured;
            _woundsLost = woundsLostSoFar;
            TotalAssignedWounds = totalAssignedWounds;
            PendingWounds = pendingWounds;
        }

        /// <summary>The ordinary pool: <paramref name="totalWoundsToAssign"/> plain wounds, one
        /// unconfined packet. Every non-Deadly caller comes through here and behaves exactly as before
        /// #401.</summary>
        public AssignWoundsResults(DataBinding<UnitData> defendingUnit, float totalWoundsToAssign)
            : this(defendingUnit, PacketsFor(totalWoundsToAssign))
        {
        }

        public AssignWoundsResults(DataBinding<UnitData> defendingUnit, IReadOnlyList<WoundPacket> packets)
        {
            _heroModelId = defendingUnit.GetValue().HeroAttachment?.HeroModelId;

            // #006: order the hero last so AutoFill (and any in-order fill) spares it until the rank and
            // file are full — the rule's "heroes are assigned wounds last, even if already wounded".
            // #023's PreAssignToAlreadyWoundedModels then walks this same order, and the TryAddWounds hero
            // guard defers the hero even if it is already wounded — so the two orderings compose correctly.
            List<DataBinding<ModelData>> alive = defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()).ToList();

            PendingWounds = new List<PendingWounds>();
            foreach (DataBinding<ModelData> model in alive.Where(model => !IsHero(model)))
            {
                PendingWounds.Add(new PendingWounds(model));
            }
            foreach (DataBinding<ModelData> model in alive.Where(IsHero))
            {
                PendingWounds.Add(new PendingWounds(model));
            }

            _packets = Meaningful(packets);

            PreAssignToAlreadyWoundedModels();
        }

        /// <summary>
        /// Single-model assignment (Takedown's "resolve as a unit of [1]"): the only recipient is
        /// <paramref name="singleModel"/>, so every packet funnels to it with no carry-over to the rest
        /// of the unit; what it cannot absorb is <see cref="WoundsLost"/>.
        /// </summary>
        public AssignWoundsResults(DataBinding<ModelData> singleModel, float totalWoundsToAssign)
            : this(singleModel, PacketsFor(totalWoundsToAssign))
        {
        }

        public AssignWoundsResults(DataBinding<ModelData> singleModel, IReadOnlyList<WoundPacket> packets)
        {
            PendingWounds = new List<PendingWounds> { new PendingWounds(singleModel) };
            _packets = Meaningful(packets);
        }

        private static List<WoundPacket> PacketsFor(float totalWoundsToAssign) =>
            totalWoundsToAssign > WoundEpsilon
                ? new List<WoundPacket> { WoundPacket.Unconfined(totalWoundsToAssign) }
                : new List<WoundPacket>();

        // A packet Regeneration emptied out has nothing to commit; dropping it here keeps every
        // "next packet" a real one, so a click always lands something.
        private static List<WoundPacket> Meaningful(IReadOnlyList<WoundPacket> packets) =>
            packets.Where(packet => packet.WeightedWounds > WoundEpsilon).ToList();

        /// <summary>
        /// Tough ordering (GDF/OPR): wounds MUST be assigned to an already-wounded (but still alive)
        /// model before any fresh one, and a wounded model must be finished before moving on. The
        /// mandatory part of that rule is enforced here, once, at construction: pour packets into each
        /// model that already carries damage from a prior activation (<see cref="ModelData.WoundsDealt"/>
        /// &gt; 0 — nothing has been assigned yet, so each entry's pending count is still 0) in turn
        /// until it would die or the packets run out. These pre-assignments are not cancellable by the
        /// player; if they consume the whole queue there is nothing left to decide and the caller can
        /// resolve without prompting. A confined packet that meets a nearly-dead model here lands what
        /// fits and loses the rest - the rule's own consequence, not a choice the defender gets.
        ///
        /// NOTE: when several models are already wounded and the queue can't finish them all, they fill
        /// in unit-list order — the defender doesn't get to choose which wounded model absorbs the
        /// shortfall. That sub-choice is deferred.
        /// </summary>
        private void PreAssignToAlreadyWoundedModels()
        {
            foreach (PendingWounds entry in PendingWounds)
            {
                if (IsFinishedAssigning) break;
                if (entry.Model.GetValue().WoundsDealt <= 0f) continue;
                while (!IsFinishedAssigning && TryAddWounds(entry.Model)) { }
            }
        }

        /// <summary>The queue, in commit order; <see cref="PacketsCommitted"/> of them are already placed.</summary>
        [JsonIgnore]
        public IReadOnlyList<WoundPacket> Packets => _packets;

        [JsonIgnore]
        public int PacketsCommitted => _packetsCommitted;

        /// <summary>The packet the next <see cref="TryAddWounds"/> commits, or null once the queue is spent.</summary>
        [JsonIgnore]
        public WoundPacket? NextPacket => _packetsCommitted < _packets.Count ? _packets[_packetsCommitted] : null;

        /// <summary>True while at least one packet carries a Deadly clump - the dialogs switch to
        /// per-clump wording on it.</summary>
        [JsonIgnore]
        public bool HasConfinedPackets => _packets.Any(packet => packet.Confined);

        /// <summary>
        /// The most the queue could ever land: every packet's weighted wounds. For a plain volley this
        /// IS the wound count; for a Deadly one it is an upper bound, since a clump's excess is lost at
        /// the model boundary. Kept as the headline figure the dialogs and tests read.
        /// </summary>
        [JsonIgnore]
        public float TotalWoundsToAssign => _packets.Sum(packet => packet.WeightedWounds);

        /// <summary>Wounds still queued: every uncommitted packet, less what has been poured of the head.</summary>
        [JsonIgnore]
        public float UncommittedWounds
        {
            get
            {
                float total = 0f;
                for (int i = _packetsCommitted; i < _packets.Count; i++) total += _packets[i].WeightedWounds;
                return total - _headPoured;
            }
        }

        /// <summary>A confined packet's excess beyond the model it landed on - the wounds Deadly says
        /// "don't carry over". Accrued as packets are committed.</summary>
        [JsonIgnore]
        public float ClumpExcessLost => _woundsLost;

        /// <summary>Everything still queued once no living model can take another wound: the unit is
        /// dead and the rest is overkill. Read, never mutated, so the same figure is reported before
        /// and after the final commit.</summary>
        [JsonIgnore]
        public float Overkill => !AllPacketsCommitted && RemainingAssignableModelCount == 0 ? UncommittedWounds : 0f;

        /// <summary>Wounds that will never land: <see cref="ClumpExcessLost"/> plus <see cref="Overkill"/>.</summary>
        [JsonIgnore]
        public float WoundsLost => ClumpExcessLost + Overkill;

        [JsonIgnore]
        public bool AllPacketsCommitted => _packetsCommitted >= _packets.Count;

        /// <summary>
        /// Living models that can still accept at least one more wound (capacity beyond what is already
        /// pending on them). With 0 or 1 such models there is no allocation for the player to decide.
        /// </summary>
        [JsonIgnore]
        public int RemainingAssignableModelCount => PendingWounds.Count(CanTakeMoreWounds);

        /// <summary>
        /// True when wounds remain to assign AND more than one model could still receive them — i.e. the
        /// player has a genuine choice. When false the caller should <see cref="AutoFill"/> the rest and
        /// skip prompting (e.g. the mandatory pre-assignment already placed every wound).
        /// </summary>
        [JsonIgnore]
        public bool HasRemainingChoice => !IsFinishedAssigning && RemainingAssignableModelCount > 1;

        /// <summary>Every packet is placed, or nothing living can take another wound (the rest is overkill).</summary>
        [JsonIgnore]
        public bool IsFinishedAssigning => AllPacketsCommitted || RemainingAssignableModelCount == 0;

        private static bool CanTakeMoreWounds(PendingWounds entry)
        {
            ModelData model = entry.Model.GetValue();
            return model.TotalWounds - model.WoundsDealt - entry.Wounds > WoundEpsilon;
        }

        /// <summary>
        /// Whether a wound can be assigned to <paramref name="entry"/>'s model right now: it must have spare
        /// capacity AND be a legal next recipient under the assignment-ordering rules — a joined hero is not
        /// assignable while any rank-and-file model can still take a wound (#006, "heroes are assigned wounds
        /// last"), and a fresh model may not be started while another model is still mid-fill (#024,
        /// "finish a wounded model before moving on"). The non-mutating predicate the UI uses to enable/gray a
        /// model; it answers the same question <see cref="TryAddWounds"/> decides before mutating, so the
        /// dialog and the engine agree.
        /// </summary>
        public bool CanAssignWoundTo(PendingWounds entry) =>
            !AllPacketsCommitted
            && CanTakeMoreWounds(entry)
            && !(IsHero(entry.Model) && AnyNonHeroHasRoom())
            && !(entry.Wounds == 0f && AnyOtherModelMidFill(entry));

        /// <summary>
        /// What <see cref="TryAddWounds"/> would land on <paramref name="entry"/>'s model right now - the
        /// next packet against that model's spare capacity - or 0 when it is not a legal recipient. The
        /// AI resolver prices its pick on this; a confined packet meeting a nearly-dead model lands only
        /// what fits.
        /// </summary>
        public float WoundsNextCommitWouldLand(PendingWounds entry)
        {
            if (!CanAssignWoundTo(entry)) return 0f;
            ModelData model = entry.Model.GetValue();
            float capacity = model.TotalWounds - model.WoundsDealt - entry.Wounds;
            return WoundAllocation.Commit(_packets[_packetsCommitted], _headPoured, capacity).Landed;
        }

        /// <summary>
        /// Commits the next packet to <paramref name="model"/>. Returns false, changing nothing, when the
        /// model is not a legal recipient (see <see cref="CanAssignWoundTo"/>) or the queue is spent.
        /// </summary>
        public bool TryAddWounds(DataBinding<ModelData> model)
        {
            PendingWounds? pendingWoundsEntry = PendingWounds.FirstOrDefault(entry => entry.Model == model);
            if ((pendingWoundsEntry == null))
            {
                throw new ArgumentOutOfRangeException($"Tried to add model to {nameof(AssignWoundsResults)} " +
                    "that was already dead or does not belong to the defending unit.");
            }

            if (AllPacketsCommitted)
            {
                return false;
            }

            // #006: a joined hero is assigned wounds last — reject it while any rank-and-file model still
            // has room. AutoFill reaches the hero (ordered last) only once the others are full, so this
            // also makes the auto path spare the hero; it gates the player-choice path against picking it.
            if (IsHero(model) && AnyNonHeroHasRoom())
            {
                return false;
            }

            // #024: "finish a wounded model before moving on" (GDF/OPR). Refuse to START assigning to a model
            // (nothing pending on it yet) while another model is already mid-fill — alive with wounds pending
            // this assignment. An unconfined packet always pours the model's full remaining capacity, so a
            // partial assignment only happens when the queue is exhausted; a confined packet smaller than
            // the model leaves it mid-fill on purpose, and this guard is what then forces the NEXT clump onto
            // the same model. The guard makes the invariant hold for ANY caller, so no future un-assign
            // affordance, AI path, or network replay can leave two models alive and partially wounded.
            if (pendingWoundsEntry.Wounds == 0f && AnyOtherModelMidFill(pendingWoundsEntry))
            {
                return false;
            }

            // #199: ONE capacity formula, and clamp to it rather than reject. The old code took the
            // amount from RemainingWoundsBinding but guarded against TotalWounds - WoundsDealt, which is
            // the binding's own double-rounded round-trip (WoundsDealt = TotalWounds - binding) - a one-ULP
            // mismatch made a legal fill look like an overfill and AutoFill faulted the game.
            float capacity = model.TotalWounds() - model.WoundsDealt() - pendingWoundsEntry.Wounds;
            if (capacity <= WoundEpsilon)
            {
                return false;
            }

            (float landed, float lost, bool consumed) =
                WoundAllocation.Commit(_packets[_packetsCommitted], _headPoured, capacity);

            pendingWoundsEntry.Wounds += landed;
            TotalAssignedWounds += landed;
            _woundsLost += lost;
            if (consumed)
            {
                _packetsCommitted++;
                _headPoured = 0f;
            }
            else
            {
                _headPoured += landed;
            }

            return true;
        }

        /// <summary>
        /// Places every remaining packet along the mandatory order (a mid-fill model first, then the
        /// rank and file in unit order, the hero last), for use when the unit will be killed or when
        /// there is no meaningful player choice (single model, zero wounds). Never throws: what the last
        /// living model cannot absorb is overkill, reported through <see cref="WoundsLost"/>.
        /// </summary>
        public void AutoFill()
        {
            while (!IsFinishedAssigning)
            {
                PendingWounds? next = PendingWounds.FirstOrDefault(CanAssignWoundTo);
                if (next == null || !TryAddWounds(next.Model)) break;
            }
        }

        /// <summary>
        /// True when placing the queue along the mandatory order would leave no living model - so the
        /// choice of order changes nothing and the caller can resolve without prompting. Runs on a copy;
        /// this object is untouched.
        /// </summary>
        public bool AutoFillWouldKillEveryModel()
        {
            AssignWoundsResults trial = Clone();
            trial.AutoFill();
            return trial.PendingWounds.All(entry => !CanTakeMoreWounds(entry));
        }

        private AssignWoundsResults Clone() =>
            new AssignWoundsResults(new List<WoundPacket>(_packets), _packetsCommitted, _headPoured, _woundsLost,
                TotalAssignedWounds,
                PendingWounds.Select(entry => new PendingWounds(entry.Model) { Wounds = entry.Wounds }).ToList())
            {
                _heroModelId = _heroModelId,
            };

        // #006 ---------------------------------------------------------------------------------------

        private bool IsHero(DataBinding<ModelData> model) =>
            _heroModelId != null && model.GetValue().ID == _heroModelId.Value;

        /// <summary> True while any non-hero pending entry can still take more wounds (capacity remaining). </summary>
        private bool AnyNonHeroHasRoom()
        {
            foreach (PendingWounds entry in PendingWounds)
            {
                if (IsHero(entry.Model)) continue;
                float remaining = entry.Model.TotalWounds() - entry.Model.WoundsDealt();
                if (entry.Wounds < remaining - WoundEpsilon) return true;
            }
            return false;
        }

        // #024 ---------------------------------------------------------------------------------------

        /// <summary> True if any model OTHER than <paramref name="entry"/> is mid-fill — alive (capacity beyond
        /// its pending wounds) yet already carrying wounds assigned this activation. While such a model exists
        /// you must finish it before starting a fresh one (GDF/OPR), so a not-yet-started model is not a legal
        /// recipient. </summary>
        private bool AnyOtherModelMidFill(PendingWounds entry)
        {
            foreach (PendingWounds other in PendingWounds)
            {
                if (other == entry) continue;
                if (other.Wounds > 0f && CanTakeMoreWounds(other)) return true;
            }
            return false;
        }
    }

    public class PendingWounds
    {
        public DataBinding<ModelData> Model { get; }
        public float Wounds { get; set; }

        public PendingWounds(DataBinding<ModelData> model)
        {
            Model = model;
            Wounds = 0;
        }
    }

}
