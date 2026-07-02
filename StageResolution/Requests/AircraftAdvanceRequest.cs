using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #029/#150 — the Aircraft forced-move decision: how far (30–36") the unit flies straight ahead along its
    /// fixed heading, or whether the player confirms flying off the table (when the chosen spot would carry a
    /// base across the table bounds; the activation then ends — no shooting — and the unit redeploys from a
    /// table edge next round). Replaces the old 30/33/36 string menu so the GUI can offer a continuous, visual
    /// placement along the segment ahead of the aircraft.
    /// </summary>
    public class AircraftAdvanceRequest : IStageTaskRequest<AircraftAdvanceResult>
    {
        public PlayerID TargetPlayerID { get; }

        public TaskID TaskID { get; }

        public string TaskName { get; }

        public DataBinding<UnitData> UnitDataBinding { get; }

        /// <summary>The unit's fixed flight heading — a unit vector in the table's X/Z plane. It can't be changed.</summary>
        public Float2 HeadingNormal { get; }

        public float MinDistanceInches { get; }

        public float MaxDistanceInches { get; }

        [JsonConstructor]
        public AircraftAdvanceRequest(PlayerID targetPlayerID, TaskID taskID, string taskName,
            DataBinding<UnitData> unitDataBinding, Float2 headingNormal,
            float minDistanceInches, float maxDistanceInches)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = taskName;
            UnitDataBinding = unitDataBinding;
            HeadingNormal = headingNormal;
            MinDistanceInches = minDistanceInches;
            MaxDistanceInches = maxDistanceInches;
        }

        public AircraftAdvanceRequest(PlayerID targetPlayerID, string taskName,
            DataBinding<UnitData> unitDataBinding, Float2 headingNormal,
            float minDistanceInches, float maxDistanceInches)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), taskName, unitDataBinding, headingNormal,
                  minDistanceInches, maxDistanceInches)
        { }

        public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceResult resolution)
        {
            return Task.FromResult(resolution);
        }
    }

    /// <summary>
    /// The chosen forced-move distance, and whether the player confirmed flying off the table. The stage treats
    /// the fly-off flag as advisory — the end-position geometry is authoritative (a stay-on answer whose bases
    /// still cross the table bounds flies off regardless; the rule offers no third option).
    /// </summary>
    public record AircraftAdvanceResult(float DistanceInches, bool FliesOffTable);
}
