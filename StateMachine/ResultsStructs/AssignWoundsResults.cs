using System;
using System.Linq;
using System.Collections.Generic;
using FDG.Data;
using FDG.Utilities;

namespace FDG
{

    public class AssignWoundsResults
    {
        public readonly float TotalWoundsToAssign;
        public float TotalAssignedWounds { get; private set; } = 0;

        //public IReadOnlyDictionary<DataBinding<ModelData>, float> PendingWounds => _pendingWounds;
        //private Dictionary<DataBinding<ModelData>, float> _pendingWounds;


        public List<PendingWounds> PendingWounds;

        public AssignWoundsResults(DataBinding<UnitData> defendingUnit, float totalWoundsToAssign)
        {
            //TODO: Add nuance of applying wounds to existing models with tough before others.
            //I'm also putting this TODO in the stage.
            /*
            _pendingWounds = new Dictionary<DataBinding<ModelData>, float>();
            foreach(DataBinding<ModelData> model in defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                _pendingWounds.Add(model, 0);
            }*/

            PendingWounds = new List<PendingWounds>();
            foreach (DataBinding<ModelData> model in defendingUnit.ModelBindings()
                .Where(model => model.GetIsAlive()))
            {
                PendingWounds.Add(new PendingWounds(model, 0));
            }


            TotalWoundsToAssign = totalWoundsToAssign;
        }

        public bool IsFinishedAssigning => TotalAssignedWounds == TotalWoundsToAssign;


        public bool TryAddWounds(DataBinding<ModelData> model, int woundsToAdd)
        {
            PendingWounds pendingWoundsEntry = PendingWounds.FirstOrDefault(entry => entry.Model == model);
            if ((pendingWoundsEntry == default))
            {
                throw new ArgumentOutOfRangeException($"Tried to add model to {nameof(AssignWoundsResults)} " +
                    "that was already dead or does not belong to the defending unit.");
            }



            if (pendingWoundsEntry.Wounds + woundsToAdd > model.TotalWounds() - model.WoundsDealt())
            {
                return false;
            }

            pendingWoundsEntry.Wounds += woundsToAdd;
            TotalAssignedWounds += woundsToAdd;

            return true;
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
                //float modelWoundsRemaining = kvp.Key.TotalWounds() - kvp.Key.WoundsDealt();
                float modelWoundsRemaining = modelData.TotalWounds - modelData.TotalWounds;

                float woundsToAssignThisModel = Math.Min(woundsToAssign, modelWoundsRemaining);
                pendingWoundsEntry.Wounds += woundsToAssignThisModel; //Might break, let's see.
                woundsToAssign -= woundsToAssignThisModel;
                TotalAssignedWounds += woundsToAssignThisModel;

                if (woundsToAssign == 0)
                {
                    break;
                }
            }

            if (IsFinishedAssigning == false)
            {
                throw new Exception($"Used {nameof(AssignWoundsResults)}.{nameof(AutoFill)} but results were not " +
                    $"finished. Required to assign: {TotalWoundsToAssign} Assigned: {TotalAssignedWounds}.");
            }
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