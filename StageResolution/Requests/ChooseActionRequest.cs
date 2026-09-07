using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #191 B1 step 5a — Choose Action becomes its own request type instead of riding
    /// <see cref="StringSelectionRequest"/> with <c>Instructions == "Choose Action"</c>. Same seam
    /// argument as <see cref="ChooseAbilityEffectRequest"/> and <see cref="ChooseSpellRequest"/>
    /// (<c>docs/ai-agent-plan.md</c> A4): AI resolvers are swapped in one request type at a time, and a
    /// shared generic type forces every resolver to tell menus apart by sniffing prompt text - which is
    /// exactly what <c>AiStringSelectionResolver</c>, <c>TacticianActionResolver</c> and
    /// <c>GunlineResolvers</c> did until this type existed.
    ///
    /// <para>It carries what the string request could not: <see cref="ActivatingUnitID"/> - the unit
    /// whose activation this menu belongs to, needed by B1's simulation seam (a prescribed activation
    /// answers Choose Action for a specific unit without re-deriving "which unit is activating" from
    /// table state) and by a future learned policy (the encoder maps options to indices against this
    /// unit's own action space, not the wire).</para>
    ///
    /// <para>The reply stays a plain <c>string</c> - the option vocabulary is already
    /// <see cref="Stages.ChooseActionStage"/>'s named constants plus rule-offer names - so every
    /// existing resolver ports by changing its request type; nothing about the DECISION changes.</para>
    /// </summary>
    public class ChooseActionRequest : IStageTaskRequest<string>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }

        /// <summary>The unit whose activation this Choose Action menu belongs to.</summary>
        public UnitID ActivatingUnitID { get; }

        public IReadOnlyList<string> ValidOptions { get; }
        public IReadOnlyList<StringSelectionRequest.InvalidOption> InvalidOptions { get; }

        /// <summary>See <see cref="StringSelectionRequest.OptionDescriptions"/> - same shape, same use
        /// (an ability action's rule-catalog description, shown as subtext).</summary>
        public Dictionary<string, string>? OptionDescriptions { get; }

        /// <summary>See <see cref="StringSelectionRequest.OptionDescriptionRules"/> - the rules an
        /// option's DESCRIPTION names, keyed the same way.</summary>
        public Dictionary<string, List<StringSelectionRequest.OptionRule>>? OptionDescriptionRules { get; }

        /// <summary>See <see cref="StringSelectionRequest.AllowCancel"/> - #248's back-out-to-unit-list
        /// affordance, offered only while the activation is pristine.</summary>
        public bool AllowCancel { get; }

        [JsonConstructor]
        public ChooseActionRequest(PlayerID targetPlayerID, TaskID taskID, UnitID activatingUnitID,
            IReadOnlyList<string> validOptions, IReadOnlyList<StringSelectionRequest.InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null, bool allowCancel = false,
            string? displayName = null,
            Dictionary<string, List<StringSelectionRequest.OptionRule>>? optionDescriptionRules = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            ActivatingUnitID = activatingUnitID;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            OptionDescriptions = optionDescriptions;
            AllowCancel = allowCancel;
            OptionDescriptionRules = optionDescriptionRules;
            TaskName = "Choosing an Action";
            DisplayName = displayName ?? TaskName;
        }

        public ChooseActionRequest(PlayerID targetPlayerID, UnitID activatingUnitID,
            IReadOnlyList<string> validOptions, IReadOnlyList<StringSelectionRequest.InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null, bool allowCancel = false,
            string? displayName = null,
            Dictionary<string, List<StringSelectionRequest.OptionRule>>? optionDescriptionRules = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), activatingUnitID, validOptions, invalidOptions,
                optionDescriptions, allowCancel, displayName, optionDescriptionRules)
        {
        }

        public Task<string> Resolve(string resolution) => Task.FromResult(resolution);
    }
}
