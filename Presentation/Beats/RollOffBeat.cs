using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// One round of a roll-off: every competitor (player/team) rolls a single d6 against the others.
    /// Unlike the generic <see cref="DiceRolledBeat"/> (a face histogram with no owners), this keeps each
    /// roll tied to its competitor so the front-end can draw a labelled stack — name on the left, that
    /// competitor's die on the right — and colour the outcome: the sole highest roller wins (green), a
    /// shared highest is a tie (yellow). A tie re-rolls, which the engine emits as a *separate*
    /// RollOffBeat for just the tied competitors, so the player sees a fresh stack for the run-off.
    /// </summary>
    [Serializable]
    public sealed class RollOffBeat : PresentationBeat
    {
        /// <summary>What the roll-off decides, e.g. "Map Side Roll-Off".</summary>
        public string Label { get; }

        public IReadOnlyList<RollOffEntry> Entries { get; }

        public RollOffBeat(string label, IReadOnlyList<RollOffEntry> entries)
        {
            Label = label;
            Entries = entries;
        }

        public override TimeSpan NominalDuration => PresentationDurations.DiceRoll;
        public override string? Text => Label;
    }

    /// <summary>How a competitor fared in a roll-off round.</summary>
    public enum ERollOffResult
    {
        Lost,
        Won,
        TiedForWin,
    }

    /// <summary>One competitor's line in a roll-off round: who, what they rolled, and how it went.</summary>
    [Serializable]
    public readonly record struct RollOffEntry(string Name, int Roll, ERollOffResult Result);
}
