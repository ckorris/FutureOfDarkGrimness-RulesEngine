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
        public string DisplayName { get; }
        public string QuestionText { get; }

        /// <summary>
        /// The default answer to this question, used by any resolver that must answer it without a live
        /// player choice — an AI controller (<see cref="FDG.Ai.Resolvers.AiYesNoResolver"/>), a CLI EOF
        /// fallback, a future timeout, or a pre-selected GUI button. Each question states its own default so
        /// the choice is deliberate per question rather than a blanket assumption. Defaults to <c>true</c>
        /// because every current question is an opt-in to a beneficial ability.
        /// <para>
        /// Kept out of the <see cref="JsonConstructor"/> and given a private setter on purpose: Newtonsoft
        /// passes a missing constructor parameter as the type default (<c>false</c>), which would silently
        /// flip the default to "no" for any request whose JSON omits the flag. As a settable property with a
        /// field initializer, an absent member leaves it at the safe <c>true</c> default while a present
        /// member round-trips its value.
        /// </para>
        /// </summary>
        [JsonProperty]
        public bool DefaultAnswer { get; private set; } = true;

        // displayName: game wording for the #318 "Waiting on" HUD line - the shared TaskName literal
        // tells a waiting opponent nothing about which of the many yes/no prompts this is.
        [JsonConstructor]
        public YesNoRequest(PlayerID targetPlayerID, TaskID taskID, string questionText,
            string? displayName = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            QuestionText = questionText;
            TaskName = "Yes/No Question";
            DisplayName = displayName ?? TaskName;
        }

        public YesNoRequest(PlayerID targetPlayerID, string questionText, bool defaultAnswer = true,
            string? displayName = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), questionText, displayName)
        {
            DefaultAnswer = defaultAnswer;
        }

        public Task<bool> Resolve(bool resolution)
        {
            return Task.FromResult(resolution);
        }
    }
} 