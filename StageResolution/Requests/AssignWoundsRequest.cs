using FDG.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class AssignWoundsRequest : IStageTaskRequest<AssignWoundsResults>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitReceivingWounds { get; }

        public float TotalWoundsToAssign { get; }


        [JsonConstructor]
        public AssignWoundsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName, 
            DataBinding<UnitData> unitReceivingWounds, float totalWoundsToAssign)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitReceivingWounds = unitReceivingWounds;
            TotalWoundsToAssign = totalWoundsToAssign;
        }

        public AssignWoundsRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitReceivingWounds, float totalWoundsToAssign)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            UnitReceivingWounds = unitReceivingWounds;
            TotalWoundsToAssign = totalWoundsToAssign;
        }

        public Task<AssignWoundsResults> Resolve(AssignWoundsResults resolution)
        {
            throw new NotImplementedException();
        }
    }
}
