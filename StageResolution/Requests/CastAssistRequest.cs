using FDG.Data;
using FDG.StageResolution;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #103 — asks one Caster's controller how many of its own spell tokens to spend on another Caster's
    /// cast, before the cast roll resolves. A friendly Caster adds +1 to the roll per token, an enemy Caster
    /// subtracts 1 per token; the reply is the token count to spend (0..<see cref="AvailableTokens"/>).
    ///
    /// Carries both units (not just names) so a GUI resolver can highlight the assisting unit on the table
    /// and draw a line to the casting unit — blue for a friendly boost, orange for an enemy disruption — and
    /// surface the assister's token count. CLI/AI resolvers just read the count.
    /// </summary>
    public class CastAssistRequest : IStageTaskRequest<int>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>The Caster being asked to spend tokens (its controller is <see cref="TargetPlayerID"/>).</summary>
        public DataBinding<UnitData> AssistingUnit { get; }

        /// <summary>The Caster whose cast is being swayed.</summary>
        public DataBinding<UnitData> CastingUnit { get; }

        /// <summary>True when the assister is friendly to the caster (+1 per token); false for an enemy (-1 per token).</summary>
        public bool IsFriendly { get; }

        /// <summary>The most tokens this assister may spend (its current spell-token pool).</summary>
        public int AvailableTokens { get; }

        public string SpellName { get; }

        [JsonConstructor]
        public CastAssistRequest(PlayerID targetPlayerID, TaskID taskID, DataBinding<UnitData> assistingUnit,
            DataBinding<UnitData> castingUnit, bool isFriendly, int availableTokens, string spellName)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            AssistingUnit = assistingUnit;
            CastingUnit = castingUnit;
            IsFriendly = isFriendly;
            AvailableTokens = availableTokens;
            SpellName = spellName;
            TaskName = "Cast Assist";
        }

        public CastAssistRequest(PlayerID targetPlayerID, DataBinding<UnitData> assistingUnit,
            DataBinding<UnitData> castingUnit, bool isFriendly, int availableTokens, string spellName)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), assistingUnit, castingUnit, isFriendly,
                   availableTokens, spellName)
        {
        }

        public Task<int> Resolve(int resolution) => Task.FromResult(resolution);
    }
}
