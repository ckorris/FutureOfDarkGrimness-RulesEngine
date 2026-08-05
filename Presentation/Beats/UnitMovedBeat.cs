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

        /// <summary>
        /// #294: the heaviest moving model's max wounds — Tough(X), or 1 for an ordinary model. A
        /// weight proxy, carried for the same reason as <see cref="AttackBeat.ArmorPenetration"/>:
        /// the front-end scales presentation by it (footfall pitch and pacing) without having to
        /// re-derive rules state, and a networked client gets it with the beat.
        /// </summary>
        public int Toughness { get; }

        // Carried (not a constant) so move pacing can later vary by distance / action type
        // (Advance vs Rush vs Charge) without changing this type or the renderer. Serializes as a
        // property so it survives the wire; the get-only override is set via the constructor.
        public override TimeSpan NominalDuration { get; }

        public UnitMovedBeat(UnitID unit, string unitName, IReadOnlyList<ModelMove> moves,
            TimeSpan nominalDuration, int toughness = 1)
        {
            Unit = unit;
            UnitName = unitName;
            Moves = moves;
            NominalDuration = nominalDuration;
            Toughness = Math.Max(1, toughness);
        }

        public override string? Text => $"{UnitName} moves.";
    }

    /// <summary>
    /// One model's move within a <see cref="UnitMovedBeat"/>: the ordered polyline it traverses.
    /// <c>Waypoints[0]</c> is the start, <c>Waypoints[^1]</c> the destination, and any points
    /// between are corners the path rounds (e.g. routing around terrain). The renderer animates
    /// along the whole polyline, distributing the beat's single duration across segments by length
    /// so the model moves at a constant speed — three segments are one move, not three.
    ///
    /// <para>#340: <paramref name="Facings"/> is the attitude at each of those points, 1:1 with
    /// <paramref name="Waypoints"/> — so <c>Facings[0]</c> is the model's PRE-MOVE resting attitude and the
    /// rest are the facings its waypoints were placed with. The renderer turns the model between them as it
    /// glides, which is the whole reason a rotation may now be dialled in for one node without applying to
    /// the ground before it: the turn has to be visible somewhere, and this is where. Null for moves that
    /// carry no per-waypoint facings (AI, aircraft) — the model simply keeps its resting attitude.</para>
    /// </summary>
    [Serializable]
    public readonly record struct ModelMove(ModelID Model, IReadOnlyList<Position> Waypoints,
        IReadOnlyList<Float2>? Facings = null);
}
