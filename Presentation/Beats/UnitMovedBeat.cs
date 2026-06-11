using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit's models moving from their current positions to new ones. The front-end glides each
    /// model along its <see cref="ModelMove.Waypoints"/> over <see cref="NominalDuration"/> instead
    /// of teleporting; the authoritative positions are already final by the time this beat is
    /// presented.
    /// </summary>
    [Serializable]
    public sealed class UnitMovedBeat : PresentationBeat
    {
        public UnitID Unit { get; }
        public string UnitName { get; }
        public IReadOnlyList<ModelMove> Moves { get; }

        // Carried (not a constant) so move pacing can later vary by distance / action type
        // (Advance vs Rush vs Charge) without changing this type or the renderer. Serializes as a
        // property so it survives the wire; the get-only override is set via the constructor.
        public override TimeSpan NominalDuration { get; }

        public UnitMovedBeat(UnitID unit, string unitName, IReadOnlyList<ModelMove> moves, TimeSpan nominalDuration)
        {
            Unit = unit;
            UnitName = unitName;
            Moves = moves;
            NominalDuration = nominalDuration;
        }

        public override string? Text => $"{UnitName} moves.";
    }

    /// <summary>
    /// One model's move within a <see cref="UnitMovedBeat"/>: the ordered polyline it traverses.
    /// <c>Waypoints[0]</c> is the start, <c>Waypoints[^1]</c> the destination, and any points
    /// between are corners the path rounds (e.g. routing around terrain). The renderer animates
    /// along the whole polyline, distributing the beat's single duration across segments by length
    /// so the model moves at a constant speed — three segments are one move, not three.
    /// </summary>
    [Serializable]
    public readonly record struct ModelMove(ModelID Model, IReadOnlyList<Position> Waypoints);
}
