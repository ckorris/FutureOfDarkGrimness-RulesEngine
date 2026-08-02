using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a list of options to the player and allows them to select one.
    /// </summary>
    /// <typeparam name="T">The type of data to select from. Must be a type registered in GameDataStore.</typeparam>
    public class SelectionRequest<T> : IStageTaskRequest<DataBinding<T>>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }
        public IReadOnlyList<ValidOption> ValidOptions { get; }
        public IReadOnlyList<InvalidOption> InvalidOptions { get; }

        /// <summary>
        /// Whether the player may cancel out of this selection (the GUI resolver's "Back" button, which
        /// resolves with a null binding). True for selections with a sensible back-destination (e.g. choosing
        /// a melee defender returns to Choose Action). False for MANDATORY selections — choosing which unit to
        /// activate/deploy, or Takedown's target model — where there is nothing to go back to; a cancel there
        /// produces a null reply the stage can't use (and crashes the networked reply path).
        /// </summary>
        public bool AllowCancel { get; }

        // displayName: what the #322 "Waiting on" HUD shows the other players; the generic TaskName
        // fallback leaks the C# type name ("Select UnitData"), so pass game wording wherever a
        // selection can hold up someone else's screen.
        [JsonConstructor]
        public SelectionRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            bool allowCancel = true, string? displayName = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            AllowCancel = allowCancel;
            TaskName = $"Select {typeof(T).Name}";
            DisplayName = displayName ?? TaskName;
        }

        public SelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            bool allowCancel = true, string? displayName = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, validOptions, invalidOptions,
                allowCancel, displayName)
        {
        }

        public Task<DataBinding<T>> Resolve(DataBinding<T> resolution)
        {
            return Task.FromResult(resolution);
        }

        public record ValidOption(DataBinding<T> Option, string Name);
        public record InvalidOption(DataBinding<T> Option, string Name, string Reason);
    }
} 