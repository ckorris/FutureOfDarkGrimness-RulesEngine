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

        [JsonConstructor]
        public AssignWoundsResults(float totalWoundsToAssign, float totalAssignedWounds, List<PendingWounds> pendingWounds)
        {
            TotalWoundsToAssign = totalWoundsToAssign;
            TotalAssignedWounds = totalAssignedWounds;
            PendingWounds = pendingWounds;
        }

        public AssignWoundsResults(DataBinding<UnitData> defendingUnit, float totalWoundsToAssign)
        {
            //TODO: Add nuance of applying wounds to existing models with tough before others.
            PendingWounds = new List<PendingWounds>();
            foreach (DataBinding<ModelData> model in defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                PendingWounds.Add(new PendingWounds(model, 0));
            }

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

            //TODO: You could split wounds in an unallowed way by assigning when there's less than its total left,
            //Then unassigning from a different model, then assigning to another tough model. Fix.
            float woundsToAdd = Math.Min(pendingWoundsEntry.Model.GetValue().RemainingWoundsBinding.GetValue(),
                TotalWoundsToAssign);

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
        /// For assigning when the unit will be killed, or debug/tests.
        /// </summary>
        public void AutoFill()
        {
            float woundsToAssign = TotalWoundsToAssign - TotalAssignedWounds;

            foreach (PendingWounds pendingWoundsEntry in PendingWounds)
            {
                ModelData modelData = pendingWoundsEntry.Model; //Shorthand.
                float modelWoundsRemaining = modelData.TotalWounds - modelData.WoundsDealt;

                float woundsToAssignThisModel = Math.Min(woundsToAssign, modelWoundsRemaining);
                pendingWoundsEntry.Wounds += woundsToAssignThisModel; //Might break, let's see.
                woundsToAssign -= woundsToAssignThisModel;
                TotalAssignedWounds += woundsToAssignThisModel;

                if (woundsToAssign == 0)
                {
                    break;
                }
            }

            /*
            //Below check fails if you actually kill off the unit.
            if (IsFinishedAssigning == false || TotalAssignedWounds == )
            {
                throw new Exception($"Used {nameof(AssignWoundsResults)}.{nameof(AutoFill)} but results were not " +
                    $"finished. Required to assign: {TotalWoundsToAssign} Assigned: {TotalAssignedWounds}.");
            }
            */
        }
    }

    public class PendingWounds
    {
        public DataBinding<ModelData> Model { get; }
        public float Wounds { get; set; }

        public PendingWounds(DataBinding<ModelData> model, float remainingWoundsPrior)
        {
            Model = model;
            Wounds = 0;
        }
    }

}