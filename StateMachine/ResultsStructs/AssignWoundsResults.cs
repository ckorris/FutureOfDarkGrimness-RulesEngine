using System;
using System.Linq;
using System.Collections.Generic;

namespace FDG
{

    public class AssignWoundsResults
    {
        public readonly float TotalWoundsToAssign;
        public float TotalAssignedWounds { get; private set; } = 0;

        public IReadOnlyDictionary<IModel, float> PendingWounds => _pendingWounds;

        private Dictionary<IModel, float> _pendingWounds;

        public AssignWoundsResults(IUnit defendingUnit, float totalWoundsToAssign)
        {
            //TODO: Add nuance of applying wounds to existing models with tough before others.
            //I'm also putting this TODO in the stage.

            _pendingWounds = new Dictionary<IModel, float>();
            foreach(IModel model in defendingUnit.Models
                .Where(model => model.GetIsAlive()))
            {
                _pendingWounds.Add(model, 0);
            }

            TotalWoundsToAssign = totalWoundsToAssign;
        }

        public bool IsFinishedAssigning => TotalAssignedWounds == TotalWoundsToAssign;


        public bool TryAddWounds(IModel model, int woundsToAdd)
        {
            if(_pendingWounds.ContainsKey(model) == false)
            {
                throw new ArgumentOutOfRangeException($"Tried to add model to {nameof(AssignWoundsResults)} " +
                    "that was already dead or does not belong to the defending unit.");
            }

            if (_pendingWounds[model] + woundsToAdd > model.TotalWounds - model.WoundsDealt)
            {
                return false;
            }

            _pendingWounds[model] += woundsToAdd;
            TotalAssignedWounds += woundsToAdd;

            return true;
        }

        /// <summary>
        /// For assigning when the unit will be killed, or debug/tests.
        /// </summary>
        public void AutoFill() 
        {
            float woundsToAssign = TotalWoundsToAssign - TotalAssignedWounds;

            foreach(KeyValuePair<IModel, float> kvp in _pendingWounds)
            {
                float modelWoundsRemaining = kvp.Key.TotalWounds - kvp.Key.WoundsDealt;

                float woundsToAssignThisModel = Math.Min(woundsToAssign, modelWoundsRemaining);
                _pendingWounds[kvp.Key] += woundsToAssignThisModel; //Might break, let's see.
                woundsToAssign -= woundsToAssignThisModel;
                TotalAssignedWounds += woundsToAssignThisModel;

                if (woundsToAssign == 0)
                {
                    break;
                }
            }

            if(IsFinishedAssigning == false)
            {
                throw new Exception($"Used {nameof(AssignWoundsResults)}.{nameof(AutoFill)} but results were not " +
                    $"finished. Required to assign: {TotalWoundsToAssign} Assigned: {TotalAssignedWounds}.");
            }
        }
    }
}