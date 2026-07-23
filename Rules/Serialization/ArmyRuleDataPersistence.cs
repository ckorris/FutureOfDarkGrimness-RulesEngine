using System.Text.Json;
using FDG.Rules.Definitions;

namespace FDG.Rules.Serialization
{
    /// <summary>
    /// #095: persists the army-FILE data an army needs at runtime — its embedded (#059) rule definitions
    /// and its spell list (#033) — as a System.Text.Json blob on <see cref="ArmyData"/>, so both survive a
    /// save/load resume.
    /// <para>
    /// The sibling of <c>RuleAttachmentPersistence</c>, and needed for the same reason: on resume the
    /// per-slot <c>ArmyListFile</c> is vestigial (the armies already live in the loaded store, so the lobby
    /// never re-sends the real list), and <c>GameBootstrap.CreateArmy</c> — the only site that reads these
    /// two lists — doesn't run. Attachment persistence covers rules ATTACHED to a carrier, but two things
    /// are named rather than attached and so died anyway:
    /// </para>
    /// <list type="bullet">
    ///   <item>A <c>RuleGrant</c> token names its rule as a STRING; the evaluator resolves that name
    ///     against the shared resolver. With only the core catalog registered, every grant of an embedded
    ///     rule logged "has no definition in the registry" and did nothing.</item>
    ///   <item><c>ArmyData.Spells</c> is <c>[JsonIgnore]</c> and only ever set at army load, so a resumed
    ///     Caster was offered an empty spell list.</item>
    /// </list>
    /// <para>
    /// Newtonsoft (the save/store layer) sees only an opaque string; STJ owns the rule/spell graph, the
    /// same division <c>RuleAttachmentPersistence</c> and <c>ArmyListUpdateMessage</c> use.
    /// </para>
    /// </summary>
    public static class ArmyRuleDataPersistence
    {
        public sealed record PersistedArmyRuleData(List<SpecialRuleDefinition> RuleDefinitions,
            List<SpellDefinition> Spells);

        public static string Serialize(IReadOnlyList<SpecialRuleDefinition> ruleDefinitions,
            IReadOnlyList<SpellDefinition> spells)
        {
            PersistedArmyRuleData data = new PersistedArmyRuleData(
                new List<SpecialRuleDefinition>(ruleDefinitions), new List<SpellDefinition>(spells));

            return JsonSerializer.Serialize(data, RuleJson.Options);
        }

        /// <summary>
        /// Reads a blob back. Null/empty yields null rather than throwing: an <see cref="ArmyData"/> built
        /// by anything other than army load (tests, a pre-#095 save) simply carries nothing to restore.
        /// </summary>
        public static PersistedArmyRuleData? Deserialize(string? json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PersistedArmyRuleData>(json, RuleJson.Options);
        }
    }
}
