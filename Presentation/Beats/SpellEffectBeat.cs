using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// #274 — which spell moment a <see cref="SpellEffectBeat"/> depicts. The front-end owns the
    /// actual look; the engine only says what happened.
    /// </summary>
    public enum ESpellVisual
    {
        /// <summary>The cast roll succeeded — plays on the caster.</summary>
        CastSuccess,

        /// <summary>The cast roll failed — plays on the caster.</summary>
        CastFailure,

        /// <summary>A successful spell landing on a target the caster meant to help.</summary>
        TargetBoon,

        /// <summary>A successful spell landing on a target the caster meant to harm.</summary>
        TargetBane,

        /// <summary>Tokens spent to make the cast MORE likely (#103 friendly assist, #244 self-boost).</summary>
        AssistBoost,

        /// <summary>Tokens spent to make the cast LESS likely (#103 enemy hinder).</summary>
        AssistHinder,
    }

    /// <summary>
    /// #274 — a spell moment worth seeing: the cast succeeding or failing on the caster, the spell
    /// landing on each target (beneficial or harmful), and the token spends that swayed the odds
    /// before the roll. One beat type covers all six because they are the same shape — an effect
    /// playing at a set of model positions, optionally fed by a set of source positions — and the
    /// front-end picks the visual and the sound from <see cref="Visual"/>.
    ///
    /// <para>
    /// Emission order in <c>CastSpellStage</c>: assist/hinder (batched, right before the roll) ->
    /// the cast roll's own <see cref="DiceRolledBeat"/> -> the result banner -> cast success/failure
    /// -> the per-target boon/bane. So the caster's outcome and what it did to the targets read as
    /// one continuous sequence.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class SpellEffectBeat : PresentationBeat
    {
        public ESpellVisual Visual { get; }

        /// <summary>
        /// Where the effect plays: the caster's living, placed models for the cast and assist
        /// visuals; every living, placed model of every chosen target for the boon/bane visuals.
        /// Never empty — the stage suppresses the beat rather than emitting a placeless one.
        /// </summary>
        public IReadOnlyList<Position> Positions { get; }

        /// <summary>
        /// Assist visuals only: the assisting/hindering casters' models, which the front-end streams
        /// INTO <see cref="Positions"/>. Empty for every other visual — and also empty for a pure
        /// #244 self-boost, which has no second unit to stream from (the front-end then draws the
        /// pulse alone).
        /// </summary>
        public IReadOnlyList<Position> Sources { get; }

        /// <summary>The spell's name, for front-ends that want to label the effect.</summary>
        public string SpellName { get; }

        /// <summary>
        /// Assist visuals: total tokens spent on this side of the roll, so the front-end can scale
        /// the effect with how hard the odds were pushed. 0 for the cast and target visuals.
        /// </summary>
        public int Magnitude { get; }

        public SpellEffectBeat(ESpellVisual visual, IReadOnlyList<Position> positions, string spellName,
            IReadOnlyList<Position>? sources = null, int magnitude = 0)
        {
            Visual = visual;
            Positions = positions;
            SpellName = spellName;
            Sources = sources ?? Array.Empty<Position>();
            Magnitude = magnitude;
        }

        public override TimeSpan NominalDuration => Visual switch
        {
            ESpellVisual.CastSuccess  => PresentationDurations.SpellCast,
            ESpellVisual.CastFailure  => PresentationDurations.SpellCast,
            ESpellVisual.TargetBoon   => PresentationDurations.SpellTarget,
            ESpellVisual.TargetBane   => PresentationDurations.SpellTarget,
            _                         => PresentationDurations.SpellAssist,
        };

        // Purely visual: the cast's own banners and log lines already narrate every one of these
        // moments (who assisted, what was rolled, what it cost), so a text projection would only
        // double up in the CLI feed.
        public override string? Text => null;
    }
}
