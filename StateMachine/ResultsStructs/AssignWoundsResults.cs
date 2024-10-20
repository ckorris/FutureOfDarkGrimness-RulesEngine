
namespace FDG
{

    public class AssignWoundsResults
    {
        public readonly int TotalWoundsToAssign;
        public int TotalAssignedWounds { get; private set; } = 0;

        public IReadOnlyDictionary<IModel, int> PendingWounds => _pendingWounds;

        private Dictionary<IModel, int> _pendingWounds;

        public AssignWoundsResults(IUnit defendingUnit, int totalWoundsToAssign)
        {
            //TODO: Add nuance of applying wounds to existing models with tough before others.
            //I'm also putting this TODO in the stage.

            _pendingWounds = new Dictionary<IModel, int>();
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
            int woundsToAssign = TotalWoundsToAssign - TotalAssignedWounds;

            foreach(KeyValuePair<IModel, int> kvp in _pendingWounds)
            {
                int modelWoundsRemaining = kvp.Key.TotalWounds - kvp.Key.WoundsDealt;

                int woundsToAssignThisModel = Math.Min(woundsToAssign, modelWoundsRemaining);
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