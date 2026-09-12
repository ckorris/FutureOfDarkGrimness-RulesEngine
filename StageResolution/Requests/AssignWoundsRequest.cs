using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    public class AssignWoundsRequest : IStageTaskRequest<AssignWoundsResults>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitReceivingWounds { get; }

        /// <summary>
        /// The wounds to place, in commit order (#401): one unconfined packet for a plain volley, one
        /// confined packet per Deadly clump. Resolvers build their <see cref="AssignWoundsResults"/> from
        /// this, which is what makes every ordering rule the engine's to enforce, not theirs.
        /// </summary>
        public List<WoundPacket> Packets { get; }

        /// <summary>The most the queue could land - the headline figure for a dialog title.</summary>
        [JsonIgnore]
        public float TotalWoundsToAssign => Packets.Sum(packet => packet.WeightedWounds);

        [JsonConstructor]
        public AssignWoundsRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitReceivingWounds, List<WoundPacket> packets)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitReceivingWounds = unitReceivingWounds;
            Packets = packets;
        }

        public AssignWoundsRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitReceivingWounds, IReadOnlyList<WoundPacket> packets)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName, unitReceivingWounds, packets.ToList())
        {
        }

        /// <summary>A plain volley: <paramref name="totalWoundsToAssign"/> wounds as one unconfined packet.</summary>
        public AssignWoundsRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitReceivingWounds, float totalWoundsToAssign)
            : this(targetPlayerID, taskName, unitReceivingWounds,
                new List<WoundPacket> { WoundPacket.Unconfined(totalWoundsToAssign) })
        {
        }

        public Task<AssignWoundsResults> Resolve(AssignWoundsResults resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
