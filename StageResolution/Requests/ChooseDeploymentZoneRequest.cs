using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// Gives the user a choice of zones to pick.
    /// </summary><remarks>
    /// Created originally for <see cref="FDG.Stages.ChooseMapSideStage"/>.
    /// </remarks>
    public class ChooseDeploymentZoneRequest : IStageTaskRequest<DataBinding<RectangularZone>>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public IReadOnlyList<DataBinding<RectangularZone>> AvailableZones { get; }
        public IReadOnlyList<DataBinding<RectangularZone>> UnavailableZones { get; }

        [JsonConstructor]
        public ChooseDeploymentZoneRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            IReadOnlyList<DataBinding<RectangularZone>> availableZones, 
            IReadOnlyList<DataBinding<RectangularZone>> unavailableZones)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            AvailableZones = availableZones;
            UnavailableZones = unavailableZones;
        }

        public ChooseDeploymentZoneRequest(PlayerID targetPlayerID,string taskName,
            IReadOnlyList<DataBinding<RectangularZone>> availableZones,
            IReadOnlyList<DataBinding<RectangularZone>> unavailableZones)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            TaskName = taskName;
            AvailableZones = availableZones;
            UnavailableZones = unavailableZones;
        }

        public Task<DataBinding<RectangularZone>> Resolve(DataBinding<RectangularZone> resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
