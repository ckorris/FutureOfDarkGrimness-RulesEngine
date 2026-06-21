using FDG.Data;
using FDG.Utilities;
using Newtonsoft.Json;

namespace FDG
{

    public class AssignWoundsResults
    {
        public readonly float TotalWoundsToAssign;

        public float TotalAssignedWounds { get; private set; } = 0;


        public List<PendingWounds> PendingWounds;

        // #006: the joined hero's model (if any). The hero is wounded last — ordered last in PendingWounds
        // (so AutoFill spares it) and rejected by TryAddWounds while any rank-and-file model has room.
        // Serialized so the guard survives a mid-assignment save/network round-trip; null for non-hero units.
        [JsonProperty] private ModelID? _heroModelId;

        [JsonConstructor]
        public AssignWoundsResults(float totalWoundsToAssign, float totalAssignedWounds, List<PendingWounds> pendingWounds)
        {
            TotalWoundsToAssign = totalWoundsToAssign;
            TotalAssignedWounds = totalAssignedWounds;
            PendingWounds = pendingWounds;
        }

        public AssignWoundsResults(DataBinding<UnitData> defendingUnit, float totalWoundsToAssign)
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

            TotalWoundsToAssign = totalWoundsToAssign;

            PreAssignToAlreadyWoundedModels();
        }

        /// <summary>
        /// Tough ordering (GDF/OPR): wounds MUST be assigned to an already-wounded (but still alive)
        /// model before any fresh one, and a wounded model must be finished before moving on. The
        /// mandatory part of that rule is enforced here, once, at construction: pour wounds into each
        /// model that already carries damage from a prior activation (<see cref="ModelData.WoundsDealt"/>
        /// &gt; 0 — nothing has been assigned yet, so each entry's pending count is still 0) in turn
        /// until it would die or the pool runs dry. These pre-assignments are not cancellable by the
        /// player; if they consume the whole pool there is nothing left to decide and the caller can
        /// resolve without prompting.
        ///
        /// NOTE: when several models are already wounded and the pool can't finish them all, they fill
        /// in unit-list order — the defender doesn't get to choose which wounded model absorbs the
        /// shortfall. That sub-choice is deferred.
        /// </summary>
        private void PreAssignToAlreadyWoundedModels()
        {
            foreach (PendingWounds entry in PendingWounds)
            {
                if (IsFinishedAssigning) break;
                if (entry.Model.GetValue().WoundsDealt > 0f)
                    TryAddWounds(entry.Model);
            }
        }

        /// <summary>
        /// Living models that can still accept at least one more wound (capacity beyond what is already
        /// pending on them). With 0 or 1 such models there is no allocation for the player to decide.
        /// </summary>
        public int RemainingAssignableModelCount => PendingWounds.Count(CanTakeMoreWounds);

        /// <summary>
        /// True when wounds remain to assign AND more than one model could still receive them — i.e. the
        /// player has a genuine choice. When false the caller should <see cref="AutoFill"/> the rest and
        /// skip prompting (e.g. the mandatory pre-assignment already placed every wound).
        /// </summary>
        public bool HasRemainingChoice => !IsFinishedAssigning && RemainingAssignableModelCount > 1;

        private static bool CanTakeMoreWounds(PendingWounds entry)
        {
            ModelData model = entry.Model.GetValue();
            return model.TotalWounds - model.WoundsDealt - entry.Wounds > 0f;
        }

        /// <summary>
        /// Whether a wound can be assigned to <paramref name="entry"/>'s model right now: it must have spare
        /// capacity AND be a legal next recipient under the assignment-ordering rules — a joined hero is not
        /// assignable while any rank-and-file model can still take a wound (#006, "heroes are assigned wounds
        /// last"). The non-mutating predicate the UI uses to enable/gray a model; it answers the same
        /// question <see cref="TryAddWounds"/> decides before mutating, so the dialog and the engine agree.
        /// </summary>
        public bool CanAssignWoundTo(PendingWounds entry) =>
            CanTakeMoreWounds(entry) && !(IsHero(entry.Model) && AnyNonHeroHasRoom());

        /// <summary>
        /// Single-model assignment (Takedown's "resolve as a unit of [1]"): the only recipient is
        /// <paramref name="singleModel"/>, so all assigned wounds funnel to it with no carry-over to
        /// the rest of the unit. Callers must cap <paramref name="totalWoundsToAssign"/> at the model's
        /// remaining wounds before constructing (AutoFill throws if it can't place every wound).
        /// </summary>
        public AssignWoundsResults(DataBinding<ModelData> singleModel, float totalWoundsToAssign)
        {
            PendingWounds = new List<PendingWounds> { new PendingWounds(singleModel) };
            TotalWoundsToAssign = totalWoundsToAssign;
        }

        public bool IsFinishedAssigning => TotalAssignedWounds == TotalWoundsToAssign;


        public bool TryAddWounds(DataBinding<ModelData> model)
        {
            PendingWounds? pendingWoundsEntry = PendingWounds.FirstOrDefault(entry => entry.Model == model);
            if ((pendingWoundsEntry == null))
            {
                throw new ArgumentOutOfRangeException($"Tried to add model to {nameof(AssignWoundsResults)} " +
                    "that was already dead or does not belong to the defending unit.");
            }

            // #006: a joined hero is assigned wounds last — reject it while any rank-and-file model still
            // has room. AutoFill reaches the hero (ordered last) only once the others are full, so this
            // also makes the auto path spare the hero; it gates the player-choice path against picking it.
            if (IsHero(model) && AnyNonHeroHasRoom())
            {
                return false;
            }

            //TODO: You could split wounds in an unallowed way by assigning when there's less than its total left,
            //Then unassigning from a different model, then assigning to another tough model. Fix.
            float woundsToAdd = Math.Min(pendingWoundsEntry.Model.GetValue().RemainingWoundsBinding.GetValue(),
                TotalWoundsToAssign - TotalAssignedWounds);

            if (pendingWoundsEntry.Wounds + woundsToAdd > model.TotalWounds() - model.WoundsDealt())
            {
                return false;
            }

            pendingWoundsEntry.Wounds += woundsToAdd;
            TotalAssignedWounds += woundsToAdd;

            return true;
        }

        public void TryRemoveWounds(DataBinding<ModelData> model)
        {
            PendingWounds? pendingWoundsEntry = PendingWounds.FirstOrDefault(entry => entry.Model == model);
            if ((pendingWoundsEntry == null))
            {
                throw new ArgumentOutOfRangeException($"Tried to add model to {nameof(AssignWoundsResults)} " +
                    "that was already dead or does not belong to the defending unit.");
            }

            float assignedWounds = pendingWoundsEntry.Wounds;
            pendingWoundsEntry.Wounds = 0;
            TotalAssignedWounds -= assignedWounds;
        }

        /// <summary>
        /// Distributes remaining wounds across models in order. For use when the unit will be killed
        /// or when there is no meaningful player choice (e.g. single model, zero wounds).
        /// </summary>
        public void AutoFill()
        {
            foreach (PendingWounds entry in PendingWounds)
            {
                if (IsFinishedAssigning) break;
                TryAddWounds(entry.Model);
            }

            if (!IsFinishedAssigning)
                throw new Exception($"{nameof(AutoFill)} could not assign all wounds. " +
                    $"Required: {TotalWoundsToAssign}, assigned: {TotalAssignedWounds}.");
        }

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
                if (entry.Wounds < remaining) return true;
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