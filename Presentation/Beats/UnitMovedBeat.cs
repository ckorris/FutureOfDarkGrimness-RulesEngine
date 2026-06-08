using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit's models moving from their current positions to new ones. The front-end glides each
    /// model From→To over <see cref="NominalDuration"/> instead of teleporting; the authoritative
    /// positions are already final by the time this beat is presented.
    /// </summary>
    [Serializable]
    public sealed class UnitMovedBeat : PresentationBeat
    {
        public UnitID Unit { get; }
        public string UnitName { get; }
        public IReadOnlyList<ModelMove> Moves { get; }

        public UnitMovedBeat(UnitID unit, string unitName, IReadOnlyList<ModelMove> moves)
        {
            Unit = unit;
            UnitName = unitName;
            Moves = moves;
        }

        public override TimeSpan NominalDuration => PresentationDurations.UnitMove;
        public override string? Text => $"{UnitName} moves.";
    }

    /// <summary>One model's move within a <see cref="UnitMovedBeat"/>: where it started and ended.</summary>
    [Serializable]
    public readonly record struct ModelMove(ModelID Model, Position From, Position To);
}
