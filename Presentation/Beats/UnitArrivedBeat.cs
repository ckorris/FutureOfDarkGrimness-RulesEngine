using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// #399 - a whole unit appears on the table from reserve (Ambush, or the return leg of Ambush
    /// Re-Deployment). Authoritative state already has every model placed when this is presented, so
    /// the front-end is free to make the arrival LOOK like an arrival - a dust cloud blooming at each
    /// model - over a single <see cref="NominalDuration"/> for the whole unit.
    ///
    /// <para>Deliberately a unit-level beat with a model list, like <see cref="UnitRoutedBeat"/> and
    /// unlike a sequence of per-model beats: every model of an ambushing unit arrives in the same
    /// instant, and playing them one after another would read as a trickle rather than a pounce.</para>
    ///
    /// <para>An arrival is a fact of the game, not a local flourish, so it rides the beat stream: the
    /// opponent watching a client sees the same cloud, at the same point in the play-by-play, as the
    /// player who placed the unit.</para>
    /// </summary>
    [Serializable]
    public sealed class UnitArrivedBeat : PresentationBeat
    {
        public UnitID Unit { get; }
        public string UnitName { get; }

        /// <summary>Where each arriving model landed. Empty is legal but nothing will be drawn.</summary>
        public IReadOnlyList<ArrivedModel> Models { get; }

        /// <summary>
        /// The rule that brought it on ("Ambush", "Rapid Ambush", "Ambush Re-Deployment"), for the
        /// beat's text form. Never null; the caller passes the alias-aware display name it already has.
        /// </summary>
        public string ReserveRuleName { get; }

        public UnitArrivedBeat(UnitID unit, string unitName, IReadOnlyList<ArrivedModel> models,
            string reserveRuleName)
        {
            Unit = unit;
            UnitName = unitName;
            Models = models;
            ReserveRuleName = reserveRuleName;
        }

        public override TimeSpan NominalDuration => PresentationDurations.UnitArrival;
        public override string? Text => $"{UnitName} arrives from {ReserveRuleName}.";
    }

    /// <summary>One model of an arriving unit: which model and where it came down.</summary>
    [Serializable]
    public readonly record struct ArrivedModel(ModelID Model, Position Position);
}
