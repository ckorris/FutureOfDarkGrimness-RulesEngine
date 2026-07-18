using System.Collections.Generic;
using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #243 — the cast action's spell picker: choose which spell to cast AND how many of the caster's own
    /// extra spell tokens to spend boosting the roll (+1 each, on top of the spell's cost). The reply is a
    /// <see cref="ChooseSpellReply"/>; a negative <see cref="ChooseSpellReply.SpellIndex"/> cancels back to
    /// Choose Action with nothing spent.
    ///
    /// <para><b>Why its own request type rather than a <see cref="StringSelectionRequest"/>.</b> Same seam
    /// argument as <see cref="ChooseAbilityEffectRequest"/>: AI resolvers are swapped in one request type at
    /// a time (<c>docs/ai-agent-plan.md</c> A4), and the Tactician previously had to recognize the spell
    /// picker by sniffing the prompt prefix. It also lets the reply carry the boost count, which a plain
    /// string choice cannot.</para>
    ///
    /// <para><b>Boost usefulness context.</b> The success threshold clamps at 1, so boosting past
    /// <c>BaseThreshold - 1</c> only matters as a hedge against enemy Casters hindering (-1 per token,
    /// prompted AFTER the caster commits). <see cref="HinderTokensInRange"/> carries how many hinder tokens
    /// enemy Casters within assist range currently hold, so a UI can gray out boost past
    /// <c>(BaseThreshold - 1) + HinderTokensInRange</c> and say why.</para>
    /// </summary>
    public class ChooseSpellRequest : IStageTaskRequest<ChooseSpellReply>
    {
        /// <summary>One offered spell. <paramref name="Label"/> is the picker label ("Name (cost)");
        /// <paramref name="Description"/> summarizes the effect, shown as subtext. Non-castable entries
        /// (unaffordable, or no legal target) are display-only, with <paramref name="UnavailableReason"/>
        /// saying why — replying with one is treated as a cancel.</summary>
        public record SpellOption(string Label, string Description, int Cost, bool Castable,
            string? UnavailableReason);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>The unit casting — display (name) and any future canvas highlighting.</summary>
        public DataBinding<UnitData> CastingUnit { get; }

        /// <summary>The caster's current spell-token pool. Boost is additionally capped by
        /// <c>AvailableTokens - Cost</c> of the chosen spell.</summary>
        public int AvailableTokens { get; }

        /// <summary>The unmodified cast success threshold (roll this or higher succeeds; base 4).</summary>
        public int BaseThreshold { get; }

        /// <summary>Total spell tokens held by enemy Casters within assist range of the caster right now —
        /// the most the roll can be hindered by (see class remarks).</summary>
        public int HinderTokensInRange { get; }

        /// <summary>All of the army's spells in stable order. The reply's index points into this list.</summary>
        public IReadOnlyList<SpellOption> Spells { get; }

        [JsonConstructor]
        public ChooseSpellRequest(PlayerID targetPlayerID, TaskID taskID, DataBinding<UnitData> castingUnit,
            int availableTokens, int baseThreshold, int hinderTokensInRange, IReadOnlyList<SpellOption> spells)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            CastingUnit = castingUnit;
            AvailableTokens = availableTokens;
            BaseThreshold = baseThreshold;
            HinderTokensInRange = hinderTokensInRange;
            Spells = spells;
            TaskName = "Choose Spell";
        }

        public ChooseSpellRequest(PlayerID targetPlayerID, DataBinding<UnitData> castingUnit,
            int availableTokens, int baseThreshold, int hinderTokensInRange, IReadOnlyList<SpellOption> spells)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), castingUnit, availableTokens, baseThreshold,
                   hinderTokensInRange, spells)
        {
        }

        public Task<ChooseSpellReply> Resolve(ChooseSpellReply resolution) => Task.FromResult(resolution);
    }

    /// <summary>
    /// Reply to <see cref="ChooseSpellRequest"/>: which spell (index into the request's
    /// <see cref="ChooseSpellRequest.Spells"/>; negative = cancel the cast, nothing spent) and how many
    /// extra tokens the caster spends boosting the roll (+1 each; the stage clamps to what remains after
    /// the spell's cost).
    /// </summary>
    public record ChooseSpellReply(int SpellIndex, int BoostTokens)
    {
        public static ChooseSpellReply Cancel => new ChooseSpellReply(-1, 0);
    }
}
