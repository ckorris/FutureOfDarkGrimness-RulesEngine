using FDG.StageResolution;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a yes/no question to the player.
    /// </summary>
    public class YesNoRequest : IStageTaskRequest<bool>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string QuestionText { get; }

        /// <summary>
        /// The answer an AI controller should give to this specific question. Each question states its own
        /// AI default explicitly so the AI's choice is deliberate per question rather than a blanket "always
        /// yes" (see <see cref="FDG.Ai.Resolvers.AiYesNoResolver"/>). Defaults to <c>true</c> because every
        /// current question is an opt-in to a beneficial ability.
        /// <para>
        /// Kept out of the <see cref="JsonConstructor"/> and given a private setter on purpose: Newtonsoft
        /// passes a missing constructor parameter as the type default (<c>false</c>), which would silently
        /// flip the AI to "no" for any request whose JSON omits the flag. As a settable property with a
        /// field initializer, an absent member leaves it at the safe <c>true</c> default while a present
        /// member round-trips its value.
        /// </para>
        /// </summary>
        [JsonProperty]
        public bool AiPrefersYes { get; private set; } = true;

        [JsonConstructor]
        public YesNoRequest(PlayerID targetPlayerID, TaskID taskID, string questionText)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            QuestionText = questionText;
            TaskName = "Yes/No Question";
        }

        public YesNoRequest(PlayerID targetPlayerID, string questionText, bool aiPrefersYes = true)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = new TaskID(Guid.NewGuid());
            QuestionText = questionText;
            AiPrefersYes = aiPrefersYes;
            TaskName = "Yes/No Question";
        }

        public Task<bool> Resolve(bool resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 