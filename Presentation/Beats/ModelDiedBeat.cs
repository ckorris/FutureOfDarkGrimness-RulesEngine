using System;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A single model destroyed. Its authoritative state is already dead when this beat is
    /// presented; the front-end plays a death animation at <see cref="Position"/> over
    /// <see cref="NominalDuration"/> before it stops drawing the model. The engine never removes
    /// dead models, so the front-end fully owns when the model visually disappears.
    /// </summary>
    [Serializable]
    public sealed class ModelDiedBeat : PresentationBeat
    {
        public ModelID Model { get; }
        public UnitID Unit { get; }
        public string UnitName { get; }
        public Position Position { get; }

        public ModelDiedBeat(ModelID model, UnitID unit, string unitName, Position position)
        {
            Model = model;
            Unit = unit;
            UnitName = unitName;
            Position = position;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ModelDeath;
        public override string? Text => $"{UnitName}: a model is destroyed.";
    }
}
