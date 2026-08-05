using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// A request that presents a list of string options to the player and allows them to select one.
    /// </summary>
    public class StringSelectionRequest : IStageTaskRequest<string>
    {
        public record InvalidOption(string Option, string Reason);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }
        public string Instructions { get; }
        public IReadOnlyList<string> ValidOptions { get; }
        public IReadOnlyList<InvalidOption> InvalidOptions { get; }

        /// <summary>
        /// Optional secondary text shown under a valid option, keyed by the option string (e.g. a spell's
        /// effect summary under its name). Options absent from the map render with no subtext; null when no
        /// option has a description (the common case — the action menu, custom actions, etc.).
        /// <para>Free-form prose about the CHOICE - what taking it costs or buys (#320's "Keeps its Limited
        /// once-per-game use for a later melee"). Special rules attached to the thing being chosen go in
        /// <see cref="OptionRules"/> instead, which is structured; see #336 for why the two are separate.</para>
        /// </summary>
        public Dictionary<string, string>? OptionDescriptions { get; }

        /// <summary>
        /// #336: the special rules carried by a valid option, keyed by that option's string - the melee
        /// weapon menu's "Deadly(3)" and "Rending", structured rather than pre-formatted into prose.
        /// <para>The option label already NAMES its rules (a weapon's datasheet line ends with them, in this
        /// same order), so this adds what the label cannot: which runs of that string are rule names, and
        /// what each one does. A front end with room to be interactive underlines the names in place and
        /// hovers the descriptions (the shoot panel and Army Forge convention, #292/#259); one without prints
        /// them as indented lines. #298 shipped the descriptions ALREADY formatted into a block, which is why
        /// the GUI could only ever dump them under the button - it had no way to know where a rule name
        /// started.</para>
        /// <para>Every rule is listed, including one whose <see cref="OptionRule.Description"/> is null
        /// because the catalog has no text for it: "this rule is not enforced" is worth showing at the moment
        /// of the choice, and it is what the shoot panel already shows. Null when no option has any rule,
        /// which is every menu but the melee weapon picker today.</para>
        /// </summary>
        public Dictionary<string, List<OptionRule>>? OptionRules { get; }

        /// <param name="Name">The rule's RESOLVED name, exactly as it appears inside the option label
        /// ("Deadly(3)", not "Deadly") - front ends locate the name in the label by matching this.</param>
        /// <param name="Description">What the rule does, or null when the catalog entry carries no
        /// player-facing text (the engine may still resolve it, or may not resolve it at all).</param>
        public record OptionRule(string Name, string? Description);

        /// <summary>
        /// #321: a companion action BELONGING to a valid option, keyed by that option's string - "and the
        /// other thing you might do about this row" (melee: hold this weapon back instead of attacking with
        /// it). The companion is still an ordinary option on the wire, listed in
        /// <see cref="ValidOptions"/> or <see cref="InvalidOptions"/> like any other and replied to by its
        /// own string; this map only says which row OWNS it.
        /// <para>Resolvers that understand it must render the companion ON its owner's row - a second
        /// button to the right, sharing the row's hotkey with a Shift modifier - and must NOT also list it
        /// as a row of its own. Two peer rows for one weapon read as two unrelated choices, which is
        /// exactly the confusion this exists to remove. A resolver that ignores the map still works: the
        /// companion is a normal option, so it simply appears in the list.</para>
        /// Null when no option has one, which is every menu but the melee weapon picker today.
        /// </summary>
        public Dictionary<string, SecondaryAction>? SecondaryActions { get; }

        /// <param name="Option">The companion's own option string - what a resolver replies with to pick it,
        /// and the key it appears under in <see cref="ValidOptions"/> / <see cref="InvalidOptions"/>.</param>
        /// <param name="ShortLabel">Two or three words for the button itself ("Hold back"). The option
        /// string carries the full weapon line, which no button in a docked panel can show.</param>
        public record SecondaryAction(string Option, string ShortLabel);

        /// <summary>
        /// #248: whether the player may back out of this menu without picking anything, replying null —
        /// the same null-cancel sentinel <see cref="SelectionRequest{T}"/> uses (legitimate over the wire,
        /// see RequestMessageSender.DeserializeAndReturnReply). Only interactive resolvers ever cancel;
        /// the CLI EOF default and the AI resolvers always return a real option, so a cancellable menu can
        /// never loop an automated player. Defaults to false — the stage must opt in AND null-check the
        /// reply, routing it to a real back-destination (ChooseActionStage -> back to unit selection).
        /// </summary>
        public bool AllowCancel { get; }

        // displayName: game wording for the #322 "Waiting on" HUD line - the shared TaskName literal
        // makes the action menu, weapon picks, and hold-or-deploy prompts indistinguishable to a
        // waiting opponent.
        [JsonConstructor]
        public StringSelectionRequest(PlayerID targetPlayerID, TaskID taskID,
            string instructions, IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null, bool allowCancel = false,
            Dictionary<string, SecondaryAction>? secondaryActions = null, string? displayName = null,
            Dictionary<string, List<OptionRule>>? optionRules = null)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            ValidOptions = validOptions;
            InvalidOptions = invalidOptions;
            OptionDescriptions = optionDescriptions;
            AllowCancel = allowCancel;
            SecondaryActions = secondaryActions;
            OptionRules = optionRules;
            TaskName = "Select Option";
            DisplayName = displayName ?? TaskName;
        }

        public StringSelectionRequest(PlayerID targetPlayerID, string instructions,
            IReadOnlyList<string> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            Dictionary<string, string>? optionDescriptions = null, bool allowCancel = false,
            Dictionary<string, SecondaryAction>? secondaryActions = null, string? displayName = null,
            Dictionary<string, List<OptionRule>>? optionRules = null)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, validOptions, invalidOptions,
                optionDescriptions, allowCancel, secondaryActions, displayName, optionRules)
        {
        }

        public Task<string> Resolve(string resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
