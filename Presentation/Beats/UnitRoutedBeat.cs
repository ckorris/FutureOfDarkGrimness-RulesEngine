using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A whole unit routs (fails morale at half strength): every still-living model is destroyed at
    /// once. Unlike a sequence of <see cref="ModelDiedBeat"/>s — which play one after another — the
    /// front-end plays the death animation for all <see cref="Models"/> simultaneously over a single
    /// <see cref="NominalDuration"/>. Authoritative state is already dead when this is presented.
    /// </summary>
    [Serializable]
    public sealed class UnitRoutedBeat : PresentationBeat
    {
        public UnitID Unit { get; }
        public string UnitName { get; }
        public IReadOnlyList<RoutedModel> Models { get; }

        public UnitRoutedBeat(UnitID unit, string unitName, IReadOnlyList<RoutedModel> models)
        {
            Unit = unit;
            UnitName = unitName;
            Models = models;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ModelDeath;
        public override string? Text => $"{UnitName} routs!";
    }

    /// <summary>One destroyed model in a rout: which model and where it fell.</summary>
    [Serializable]
    public readonly record struct RoutedModel(ModelID Model, Position Position);
}
