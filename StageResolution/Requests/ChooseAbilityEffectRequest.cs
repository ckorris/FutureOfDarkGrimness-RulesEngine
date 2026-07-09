using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// "Pick one of this rule's effects." Raised when a single rule offers several activated abilities at the
    /// same hook and the player must choose exactly one — the corpus' Versatile Attack / Watchborn /
    /// Versatile Reach shape ("when this unit is activated, pick one effect: ... either AP(+1) or +1 to hit").
    ///
    /// <para><b>Why its own request type rather than a <see cref="StringSelectionRequest"/>.</b> The Tactician
    /// plan (<c>docs/ai-agent-plan.md</c>, phase A4) replaces AI resolvers <i>one request type at a time</i>,
    /// so the request type is the seam an agent is swapped in at. Riding StringSelectionRequest would force a
    /// future agent to take over Choose Action and the pre-attack menu at the same time, and to tell them
    /// apart by sniffing prompt text — which is exactly what <c>AiStringSelectionResolver</c> does today
    /// (<c>if (request.Instructions == "Choose Action")</c>). A distinct type also gives a learned policy a
    /// clean action space: the reply is the chosen option's index.</para>
    ///
    /// <para>The options are plain data, not live <c>ActivatedAbility</c> objects: a request crosses the wire
    /// through Newtonsoft, and an ability carries a polymorphic <c>Effect</c> graph that only the
    /// System.Text.Json rule schema knows how to round-trip. An in-process agent that wants the effect graph
    /// can look <see cref="RuleName"/> up in the rule catalog.</para>
    ///
    /// Mandatory: there is no cancel and no "none of these". The stage that raises it has already checked
    /// that at least two options are affordable and available.
    /// </summary>
    public class ChooseAbilityEffectRequest : IStageTaskRequest<int>
    {
        /// <summary>One selectable effect. <paramref name="Label"/> is the ability's authored label;
        /// <paramref name="Description"/> is its rule's description, shown as subtext.</summary>
        public record EffectOption(string Label, string Description);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string Instructions { get; }

        /// <summary>The rule offering the choice — display, and the key an in-process agent can use to
        /// recover the full ability definitions from the rule catalog.</summary>
        public string RuleName { get; }

        /// <summary>The unit whose activation this choice belongs to. Display only.</summary>
        public string BearerName { get; }

        /// <summary>At least two, in the order the rule declares them. The reply is an index into this.</summary>
        public IReadOnlyList<EffectOption> Options { get; }

        [JsonConstructor]
        public ChooseAbilityEffectRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            string ruleName, string bearerName, IReadOnlyList<EffectOption> options)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            RuleName = ruleName;
            BearerName = bearerName;
            Options = options;
            TaskName = "Choose Effect";
        }

        public ChooseAbilityEffectRequest(PlayerID targetPlayerID, string instructions, string ruleName,
            string bearerName, IReadOnlyList<EffectOption> options)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, ruleName, bearerName, options)
        {
        }

        public Task<int> Resolve(int resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
